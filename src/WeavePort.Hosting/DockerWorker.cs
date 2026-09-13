using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace WeavePort.Hosting;
internal sealed class DockerWorker(DockerProfile profile, string version, TimeProvider clock) : Worker(profile, version)
{
    private DockerProfile Deployment => (DockerProfile)Profile;

    private readonly SemaphoreSlim _cleanup = new(1);
    private Process? _process;
    private WorkerSocket? _socket;
    private Task? _drain;
    internal override Stream Input => _socket is not null ? _socket.Stream : _process!.StandardInput.BaseStream;
    internal override bool Running => _socket is not null ? _socket.Running : _process is { HasExited: false };

    private bool _removed;
    internal override async Task StartAsync(CancellationToken token)
    {
        string memory = Profile.MemoryMiB.ToString(CultureInfo.InvariantCulture) + "m";
        List<string> arguments = ["run", "--name", Instance, "--label", "weaveport.poc=true", "--network", "none", "--read-only", "--cap-drop", "ALL", "--security-opt", "no-new-privileges", "--user", "65532:65532", "--memory", memory, "--memory-swap", memory, "--cpus", Deployment.CpuCount.ToString(CultureInfo.InvariantCulture), "--pids-limit", "64", "--tmpfs", "/tmp:rw,noexec,nosuid,size=16m,mode=1777", "--log-driver", "none"];
        if (Deployment.SocketTransport is { } transport)
        {
            _socket = new WorkerSocket(transport, Instance);
            arguments.AddRange(["--detach", "--env", "WEAVEPORT_SOCKET=/run/weaveport/p.sock", "--mount", "type=bind,source=" + Path.Combine(transport.DockerDirectory, Instance) + ",target=/run/weaveport,readonly", Deployment.Image]);
            await DockerCommand.RunAsync(Deployment.Context, arguments.ToArray(), token);
            await _socket.AcceptAsync(token);
            Reader = new Frames(_socket.Stream);
        }
        else
        {
            arguments.AddRange(["--interactive", Deployment.Image]);
            _process = DockerCommand.Start(Deployment.Context, arguments);
            _drain = DrainAsync(_process.StandardError);
            Reader = new Frames(_process.StandardOutput.BaseStream);
        }

        JsonElement ready = await Reader.ReadAsync(token);
        WorkerEnvelope.ValidateReady(ready, Version);
        ReadyAt = clock.GetTimestamp();
    }

    internal override async Task<JsonElement> DestroyAsync()
    {
        await _cleanup.WaitAsync();
        try
        {
            if (_removed)
            {
                _socket?.RemoveEndpoint();
                return JsonSerializer.SerializeToElement(new { });
            }

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            JsonElement result = JsonSerializer.SerializeToElement(new { });
            // Capture diagnostics when possible; removal must still run when inspection fails.
            try
            {
                JsonElement state = JsonElement.Parse(await DockerCommand.RunAsync(Deployment.Context, ["inspect", "--format", "{{json .State}}", Instance], deadline.Token));
                result = JsonSerializer.SerializeToElement(new { exitCode = state.GetProperty("ExitCode").GetInt32(), oomKilled = state.GetProperty("OOMKilled").GetBoolean(), runningAtTermination = state.GetProperty("Running").GetBoolean() });
            }
            catch (IOException)
            { /* Startup may fail before a container exists; absence is checked below. */
            }

            try
            {
                await DockerCommand.RunAsync(Deployment.Context, ["rm", "--force", Instance], deadline.Token);
            }
            catch (IOException)
            {
                string remaining = await DockerCommand.RunAsync(Deployment.Context, ["ps", "--all", "--quiet", "--filter", "name=^/" + Instance + "$"], deadline.Token);
                if (remaining.Length != 0)
                {
                    throw;
                }
            }

            _removed = true;
            _socket?.RemoveEndpoint();
            return result;
        }
        finally
        {
            try
            {
                await ReleaseProcessAsync();
            }
            finally
            {
                if (_socket is not null)
                {
                    Reader?.Dispose();
                    _socket.Dispose();
                }

                _cleanup.Release();
            }
        }
    }

    private async Task ReleaseProcessAsync()
    {
        if (_process is not null)
        {
            if (!_process.HasExited)
            {
                _process.Kill(true);
            }

            await _process.WaitForExitAsync();
            if (_drain is not null)
            {
                await _drain;
            }

            Reader?.Dispose();
            _process.Dispose();
            _process = null;
        }
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer) > 0)
        {
        }
    }
}
