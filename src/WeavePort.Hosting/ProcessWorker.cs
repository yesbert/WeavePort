using System.Diagnostics;
using System.Text.Json;

namespace WeavePort.Hosting;
internal sealed class ProcessWorker(ProcessProfile profile, string version, TimeProvider clock) : Worker(profile, version)
{
    private readonly SemaphoreSlim _cleanup = new(1);
    private Process? _process;
    private Task? _drain;
    private ProcessSocket? _socket;
    private string? _workspace;
    private int _pid;
    private bool _removed;
    internal override string Instance => base.Instance + "-p" + _pid;
    internal override Stream Input => _socket?.Stream ?? _process!.StandardInput.BaseStream;
    internal override bool Running => _process is { HasExited: false };

    internal override async Task StartAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        CreateWorkspace();
        ProcessStartInfo info = CreateStartInfo();
        if (profile.UseUnixSocket)
        {
            _socket = new ProcessSocket();
            _socket.Listen(profile.SocketBufferBytes);
            info.Environment["WEAVEPORT_SOCKET"] = _socket.Path;
        }

        try
        {
            _process = Process.Start(info) ?? throw new IOException("Local worker did not start.");
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            throw new IOException("Local worker executable could not start.", error);
        }

        _pid = _process.Id;
        _drain = DrainAsync(_process.StandardError);
        if (_socket is not null)
        {
            _drain = Task.WhenAll(_drain, DrainAsync(_process.StandardOutput));
            await _socket.AcceptAsync(token);
        }

        Reader = new Frames(_socket?.Stream ?? _process.StandardOutput.BaseStream);
        WorkerEnvelope.ValidateReady(await Reader.ReadAsync(token), Version);
        ReadyAt = clock.GetTimestamp();
    }

    private void CreateWorkspace()
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(profile.WorkspaceRoot);
        }
        else
        {
            Directory.CreateDirectory(profile.WorkspaceRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            if ((File.GetUnixFileMode(profile.WorkspaceRoot) & (UnixFileMode)63) != 0)
            {
                throw new IOException("Local workspace parent must be private.");
            }
        }

        if (new DirectoryInfo(profile.WorkspaceRoot).LinkTarget is not null)
        {
            throw new IOException("Workspace parent must not be a symbolic link.");
        }

        _workspace = Path.Combine(profile.WorkspaceRoot, base.Instance);
        if (Directory.Exists(_workspace))
        {
            throw new IOException("Workspace already exists.");
        }

        Directory.CreateDirectory(_workspace);
    }

    private ProcessStartInfo CreateStartInfo()
    {
        var info = new ProcessStartInfo(profile.Executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _workspace,
            CreateNoWindow = true
        };
        foreach (string argument in profile.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment.Clear();
        if (OperatingSystem.IsWindows())
        {
            info.Environment["SystemRoot"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        }

        info.Environment["PATH"] = Path.GetDirectoryName(profile.Executable) + Path.PathSeparator + (OperatingSystem.IsWindows() ? Environment.GetFolderPath(Environment.SpecialFolder.System) : "/usr/bin:/bin");
        info.Environment["HOME"] = _workspace;
        info.Environment["TMPDIR"] = _workspace;
        info.Environment["TEMP"] = _workspace;
        info.Environment["TMP"] = _workspace;
        info.Environment["WEAVEPORT_WORKSPACE"] = _workspace;
        info.Environment["DOTNET_EnableDiagnostics"] = "0";
        info.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        return info;
    }

    internal override async Task<JsonElement> DestroyAsync()
    {
        await _cleanup.WaitAsync();
        try
        {
            if (_removed)
            {
                return JsonSerializer.SerializeToElement(new { });
            }

            int? exitCode = null;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            if (_process is not null)
            {
                bool running = !_process.HasExited;
                if (running)
                {
                    _process.Kill(entireProcessTree: true);
                }

                await _process.WaitForExitAsync(deadline.Token);
                exitCode = _process.ExitCode;
                if (_drain is not null)
                {
                    await _drain.WaitAsync(deadline.Token);
                }
            }

            _socket?.Dispose();
            if (_workspace is not null && Directory.Exists(_workspace))
            {
                Directory.Delete(_workspace, recursive: true);
            }

            Reader?.Dispose();
            _process?.Dispose();
            _process = null;
            _removed = true;
            return JsonSerializer.SerializeToElement(new { exitCode, runningAtTermination = false, cleanupScope = "root-exited-best-effort-process-tree" });
        }
        finally
        {
            _cleanup.Release();
        }
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer) > 0)
        {
        // Consume stderr without retaining plugin-controlled diagnostic content.
        }
    }
}
