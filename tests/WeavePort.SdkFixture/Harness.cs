using System.Diagnostics;
using System.Text.Json;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;
using WeavePort.Sdk.Gateway;

namespace WeavePort.SdkFixture;

public sealed class Harness : IAsyncDisposable
{
    private PluginHost? _host;
    private Process? _gateway;
    private Task<string>? _stderr;
    private LocalPluginClient[] _local = [];
    private string _workspace = "";
    public IPluginClient[] Clients { get; private set; } = [];
    public Uri? GatewayAddress { get; private set; }
    public int GatewayId => _gateway?.Id ?? 0;
    public static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException("Missing " + name);
    public static string Output => Required("WP_SDK_OUTPUT");

    public static async Task<Harness> CreateAsync(string[] languages, bool remote, bool socket = false)
    {
        var harness = new Harness();
        try
        {
            Configuration configuration = Configure(languages, socket);
            harness._workspace = configuration.Workspace;
            Directory.CreateDirectory(Output);
            VerifyLoaded();
            if (!remote)
            {
                harness._host = Fixture.Host();
                harness._local = await Fixture.BindAsync(configuration, harness._host);
                harness.Clients = harness._local;
            }
            else
            {
                var start = new ProcessStartInfo(Required("WP_SDK_DOTNET")) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                start.ArgumentList.Add(Path.Combine(Required("WP_SDK_ROOT"), "artifacts/sdk-worker-host/WeavePort.WorkerHost.dll"));
                start.Environment.Clear();
                start.Environment["DOTNET_ROLL_FORWARD"] = "LatestPatch";
                harness._gateway = Process.Start(start) ?? throw new IOException("Gateway start failed.");
                harness._stderr = harness._gateway.StandardError.ReadToEndAsync();
                await harness._gateway.StandardInput.WriteLineAsync(JsonSerializer.Serialize(configuration));
                using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                string readyLine = await harness._gateway.StandardOutput.ReadLineAsync(startup.Token) ?? throw new IOException("Gateway exited: " + await harness._stderr);
                Ready ready = JsonSerializer.Deserialize<Ready>(readyLine)!;
                foreach (var loaded in Fixture.Loaded())
                    if (ready.Loaded[loaded.Key] != loaded.Value) throw new InvalidDataException("Gateway/consumer binary mismatch.");
                harness.GatewayAddress = new Uri(ready.Address);
                harness.Clients = ready.Credentials.Select(token => (IPluginClient)new RemotePluginClient(new Uri(ready.Address), token)).ToArray();
            }
            await File.WriteAllTextAsync(Path.Combine(Output, $"identity-{Environment.ProcessId}-{Guid.NewGuid():N}.json"), JsonSerializer.Serialize(new
            {
                remote, socket, languages, loaded = Fixture.Loaded(), artifacts = configuration.Artifacts,
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                serverGc = System.Runtime.GCSettings.IsServerGC, gateway = harness.GatewayId
            }));
            return harness;
        }
        catch { await harness.DisposeAsync(); throw; }
    }
    public static void VerifyLoaded()
    {
        string root = Required("WP_SDK_ROOT");
        foreach (var loaded in Fixture.Loaded())
            if (loaded.Value != Fixture.Hash(Path.Combine(root, "artifacts/sdk-worker-host", loaded.Key + ".dll")))
                throw new InvalidDataException("Loaded SDK package identity mismatch.");
    }
    private static Configuration Configure(string[] languages, bool socket)
    {
        string root = Required("WP_SDK_ROOT");
        Provider[] providers =
        [
            new("csharp", Required("WP_SDK_DOTNET"), [Path.Combine(root, "artifacts/sdk-csharp/ExamplePlugin.dll")]),
            new("python", Path.Combine(root, "artifacts/sdk-python/bin/python"), [Path.Combine(root, "artifacts/sdk-python-example/plugin.py")]),
            new("typescript", Required("WP_SDK_NODE"), [Path.Combine(root, "examples/sdk/typescript/dist/plugin.js")])
        ];
        string pythonSdk = Directory.GetFiles(Path.Combine(root, "artifacts/sdk-python/lib"), "__init__.py", SearchOption.AllDirectories).Single(p => p.EndsWith("weaveport_sdk/__init__.py", StringComparison.Ordinal));
        string[] artifacts = [.. providers.Select(p => p.Arguments[0]), Path.Combine(root, "artifacts/sdk-csharp/WeavePort.Sdk.dll"), pythonSdk, Path.Combine(root, "examples/sdk/typescript/node_modules/@weaveport/sdk/dist/index.js")];
        return new(providers, languages.Select((language, index) => new Binding("tenant-" + index, language)).ToArray(), Path.Combine(Output, "workers-" + Guid.NewGuid().ToString("N")), socket, artifacts.ToDictionary(p => p, Fixture.Hash));
    }
    public async Task<int[]> WorkerIdsAsync()
    {
        if (_gateway is null) return Fixture.WorkerIds(_local);
        await _gateway.StandardInput.WriteLineAsync("snapshot");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        string line = await _gateway.StandardOutput.ReadLineAsync(deadline.Token) ?? throw new IOException("Gateway disconnected.");
        return JsonSerializer.Deserialize<int[]>(line)!;
    }
    public async ValueTask DisposeAsync()
    {
        foreach (var client in Clients) await client.DisposeAsync();
        if (_host is not null) await _host.DisposeAsync();
        if (_gateway is not null)
        {
            try
            {
                if (!_gateway.HasExited)
                {
                    await _gateway.StandardInput.WriteLineAsync("quit");
                    await _gateway.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }
                if (_gateway.ExitCode != 0) throw new IOException("Gateway exit: " + (_stderr is null ? "" : await _stderr));
            }
            finally
            {
                if (!_gateway.HasExited) { _gateway.Kill(entireProcessTree: true); await _gateway.WaitForExitAsync(); }
                _gateway.Dispose();
            }
        }
        if (Directory.Exists(_workspace)) Directory.Delete(_workspace);
    }
}
