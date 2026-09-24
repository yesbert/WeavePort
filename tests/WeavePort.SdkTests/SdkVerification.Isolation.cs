using System.Diagnostics;
using System.Text.Json;
using WeavePort.Sdk.Client;
using WeavePort.Sdk.Gateway;
using WeavePort.SdkFixture;

internal sealed partial class SdkVerification
{
    private async Task CrashIsolationAsync(IPluginClient client, string tenant, Harness harness, int lane)
    {
        bool crashed = false;
        try
        {
            await client.CallAsync("crash", JsonSerializer.SerializeToElement(new
            {
            }));
        }
        catch (PluginCallException) { crashed = true; }
        Assert(crashed, "Worker crash surfaced");
        int victim = (lane + 1) % harness.Clients.Length;
        Workload.Check(await Workload.ExecuteAsync(harness.Clients[victim], "callback"), "callback", "tenant-" + victim);
        _checks++;
        Workload.Check(await Workload.ExecuteAsync(client, "tiny"), "tiny", tenant);
        _checks++;
    }

    private async Task VerifyPairAsync(bool socket, bool remote, string language)
    {
        await using var pair = await Harness.CreateAsync([language, language], remote, socket);
        IPluginClient attacker = pair.Clients[0], victim = pair.Clients[1];
        Workload.Check(await Workload.ExecuteAsync(attacker, "callback"), "callback", "tenant-0");
        _checks++;
        Workload.Check(await Workload.ExecuteAsync(victim, "callback"), "callback", "tenant-1");
        _checks++;
        for (int i = 0; i < 2; i++)
        {
            await ConsumeAsync(victim, Workload.Query(2, 3), default);
        }

        Assert((await victim.CallAsync("state", JsonSerializer.SerializeToElement(new
        {
        }))).GetProperty("closed").GetInt32() == 2, "Victim state established");
        bool crashed = false;
        try
        {
            await attacker.CallAsync("crash", JsonSerializer.SerializeToElement(new
            {
            }));
        }
        catch (PluginCallException) { crashed = true; }
        Assert(crashed, "Same-artifact attacker crash");
        Workload.Check(await Workload.ExecuteAsync(victim, "callback"), "callback", "tenant-1");
        _checks++;
        Assert((await victim.CallAsync("state", JsonSerializer.SerializeToElement(new
        {
        }))).GetProperty("closed").GetInt32() == 2, "Victim state survives attacker crash");
        using (var cancel = new CancellationTokenSource(80))
        {
            bool cancelled = false;
            try
            {
                await ConsumeAsync(attacker, Workload.Query(10000, 64, delayMs: 10), cancel.Token);
            }
            catch (OperationCanceledException) { cancelled = true; }
            Assert(cancelled, "Same-artifact attacker cancellation");
        }
        Workload.Check(await Workload.ExecuteAsync(victim, "callback"), "callback", "tenant-1");
        _checks++;
        Workload.Check(await Workload.ExecuteAsync(attacker, "tiny"), "tiny", "tenant-0");
        _checks++;
    }

    private async Task VerifyUnresponsiveGatewayAsync()
    {
        // A gateway that accepts TCP but never responds must not create an unbounded SDK call.
        using (var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0))
        {
            listener.Start();
            var endpoint = new Uri("http://127.0.0.1:" + ((System.Net.IPEndPoint)listener.LocalEndpoint).Port);
            await using var client = new RemotePluginClient(endpoint, "synthetic-deadline-test", TimeSpan.FromMilliseconds(100),
                new PluginStreamOptions
                {
                    ExchangeTimeout = TimeSpan.FromMilliseconds(100),
                    TotalTimeout = TimeSpan.FromSeconds(1)
                });
            bool cancelled = false;
            var clock = Stopwatch.StartNew();
            try
            {
                await Workload.ExecuteAsync(client, "tiny");
            }
            catch (OperationCanceledException) { cancelled = true; }
            Assert(cancelled && clock.Elapsed < TimeSpan.FromSeconds(5), "Unresponsive gateway call timeout");
            cancelled = false;
            clock.Restart();
            try
            {
                await ConsumeAsync(client, Workload.Query(1, 1), default);
            }
            catch (OperationCanceledException) { cancelled = true; }
            Assert(cancelled && clock.Elapsed < TimeSpan.FromSeconds(10), "Unresponsive gateway stream and cleanup timeout");
        }
    }
}
