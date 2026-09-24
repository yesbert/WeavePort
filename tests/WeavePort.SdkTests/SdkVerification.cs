using System.Diagnostics;
using System.Text.Json;
using WeavePort.Sdk.Client;
using WeavePort.Sdk.Gateway;
using WeavePort.SdkFixture;

internal sealed partial class SdkVerification
{
    private int _checks;
    private static readonly string[] Languages = ["csharp", "python", "typescript"];

    internal static Task RunAsync() => new SdkVerification().RunAllAsync();

    private async Task RunAllAsync()
    {
        var outcomes = new List<object>();
        var topologies = from socket in new[] { false, true }
                         from remote in new[] { false, true }
                         select (socket, remote);
        foreach (var (socket, remote) in topologies)
        {
            outcomes.AddRange(await VerifyTopologyAsync(socket, remote));
        }
        var pairOutcomes = new List<object>();
        var pairs = from topology in topologies
                    from language in Languages
                    select (topology.socket, topology.remote, language);
        foreach (var (socket, remote, language) in pairs)
        {
            await VerifyPairAsync(socket, remote, language);
            pairOutcomes.Add(new
            {
                socket,
                remote,
                language,
                passed = true
            });
        }
        await VerifyUnresponsiveGatewayAsync();
        await File.WriteAllTextAsync(Path.Combine(Harness.Output, "verification.json"),
            JsonSerializer.Serialize(new
            {
                checks = _checks,
                outcomes,
                pairOutcomes,
                passed = true
            }));
        Console.WriteLine($"Passed {_checks} SDK checks.");
    }

    private async Task<List<object>> VerifyTopologyAsync(bool socket, bool remote)
    {
        await using var harness = await Harness.CreateAsync(Languages, remote, socket);
        if (remote)
        {
            await VerifyAuthenticationAsync(harness.GatewayAddress!);
        }
        var outcomes = new List<object>();
        for (int lane = 0; lane < harness.Clients.Length; lane++)
        {
            await VerifyClientAsync(harness, lane, socket);
            outcomes.Add(new
            {
                remote,
                socket,
                language = Languages[lane],
                passed = true
            });
        }
        return outcomes;
    }

    private async Task VerifyClientAsync(Harness harness, int lane, bool socket)
    {
        string tenant = "tenant-" + lane;
        IPluginClient client = harness.Clients[lane];
        await CompleteWorkloadsAsync(client, tenant);
        await TypedResultsAsync(client, tenant);
        await StreamProgressAsync(client, tenant);
        await StreamCancellationAsync(client, tenant);
        await StreamFailuresAsync(client, tenant);
        if (!socket)
        {
            await TotalQuotaAsync(client, tenant);
        }
        await CallbackAuthorityAsync(client, tenant);
        await SingleFlightAsync(client, tenant);
        await CrashIsolationAsync(client, tenant, harness, lane);
    }

    private async Task VerifyAuthenticationAsync(Uri address)
    {
        await using var invalid = new RemotePluginClient(address, "invalid-credential");
        bool rejected = false;
        try
        {
            await Workload.ExecuteAsync(invalid, "tiny");
        }
        catch (PluginCallException error) when (error.Status == "binding-denied")
        {
            rejected = true;
        }
        Assert(rejected, "Gateway authentication");
    }
    private void Assert(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidDataException(name);
        }
        _checks++;
    }
    static async Task ConsumeAsync(IPluginClient client, JsonElement query, CancellationToken token)
    {
        await foreach (var unused in client.StreamAsync("records", query, token))
        {
        }
    }

}
