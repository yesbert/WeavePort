using System.Security.Cryptography;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;

namespace WeavePort.SdkFixture;

public sealed record Provider(string Language, string Executable, string[] Arguments);
public sealed record Binding(string Tenant, string Language);
public sealed record Configuration(Provider[] Providers, Binding[] Bindings, string Workspace, bool Socket, Dictionary<string, string> Artifacts, int MaximumConcurrentStarts = 8);
public sealed record Ready(string Address, string[] Credentials, int Pid, Dictionary<string, string> Loaded);

public static class Fixture
{
    public static PluginHost Host() => new(options: new WorkerPoolOptions(MaximumPristineWorkers: 0));
    public static Dictionary<string, string> Loaded() => new[] { typeof(PluginHost).Assembly, typeof(IPluginSession).Assembly, typeof(LocalPluginClient).Assembly, typeof(WeavePort.Sdk.Gateway.GatewayRegistry).Assembly }
        .ToDictionary(a => a.GetName().Name!, a => Hash(a.Location));
    public static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    public static void Verify(Configuration configuration)
    {
        foreach (var artifact in configuration.Artifacts)
            if (Hash(artifact.Key) != artifact.Value) throw new InvalidDataException("Configured SDK artifact changed.");
    }
    public static async Task<LocalPluginClient[]> BindAsync(Configuration configuration, PluginHost host)
    {
        Verify(configuration);
        var clients = new List<LocalPluginClient>();
        foreach (Binding binding in configuration.Bindings)
        {
            Provider provider = configuration.Providers.Single(p => p.Language == binding.Language);
            var profile = new ProcessProfile(provider.Executable, provider.Arguments, true, configuration.Workspace, timeout: TimeSpan.FromSeconds(3)) { UseUnixSocket = configuration.Socket };
            var session = await host.BindAsync(new PluginContext(binding.Tenant, "sdk-example", "1", "default", JsonSerializer.SerializeToElement(new { })), profile, new Callbacks(), ["host.owner"]);
            clients.Add(new LocalPluginClient(session));
        }
        return clients.ToArray();
    }
    public static int[] WorkerIds(IEnumerable<LocalPluginClient> clients) => clients.Where(c => c.Instance.Contains("-p", StringComparison.Ordinal))
        .Select(c => int.Parse(c.Instance[(c.Instance.LastIndexOf("-p", StringComparison.Ordinal) + 2)..])).ToArray();
    private sealed class Callbacks : IHostCallbacks
    {
        public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (call.Operation != "host.owner") throw new UnauthorizedAccessException();
            return ValueTask.FromResult(JsonSerializer.SerializeToElement(new { owner = call.Context.Tenant, secret = "synthetic-" + call.Context.Tenant }));
        }
    }
}
