using System.Diagnostics;
using System.Text.Json;
using WeavePort.Sdk.Client;
using WeavePort.Sdk.Gateway;
using WeavePort.SdkFixture;

internal sealed partial class SdkVerification
{
    private async Task StreamProgressAsync(IPluginClient client, string tenant)
    {
        int received = 0;
        await foreach (var row in client.StreamAsync("records", Workload.Query(41, 37, callbacks: true)))
        {
            Workload.CheckRow(row, received++, 37, tenant);
        }

        Assert(received == 41, "Callbacks across batches");
        await foreach (var unused in client.StreamAsync("records", Workload.Query(0, 0)))
        {
            throw new InvalidDataException("Expected empty stream.");
        }

        _checks++;
        await foreach (var unused in client.StreamAsync("records", Workload.Query(10000, 64, delayMs: 1)))
        {
            break;
        }

        Workload.Check(await Workload.ExecuteAsync(client, "tiny"), "tiny", tenant);
        _checks++;
    }

    private async Task StreamCancellationAsync(IPluginClient client, string tenant)
    {
        using (var cancel = new CancellationTokenSource(80))
        {
            bool cancelled = false;
            try
            {
                await foreach (var unused in client.StreamAsync("records", Workload.Query(10000, 64, delayMs: 10), cancel.Token))
                {
                }
            }
            catch (OperationCanceledException) { cancelled = true; }
            Assert(cancelled, "Cancellation surfaced");
        }
        Workload.Check(await Workload.ExecuteAsync(client, "callback"), "callback", tenant);
        _checks++;
    }

    private async Task StreamFailuresAsync(IPluginClient client, string tenant)
    {
        foreach (var query in new[] { Workload.Query(100, 32, failAt: 20), Workload.Query(1, 150000) })
        {
            bool failed = false;
            try
            {
                await ConsumeAsync(client, query, default);
            }
            catch (IOException) { failed = true; }
            Assert(failed, "Partial/oversized result fails");
        }
    }

    private async Task TotalQuotaAsync(IPluginClient client, string tenant)
    {
        bool limited = false;
        int delivered = 0;
        try
        {
            await foreach (var unused in client.StreamAsync("records", Workload.Query(9000, 8192)))
            {
                delivered++;
            }
        }
        catch (IOException) { limited = true; }
        Assert(limited && delivered > 0 && delivered < 9000, "Total stream quota with visible partial output");
    }

    private async Task SingleFlightAsync(IPluginClient client, string tenant)
    {
        using (var cancel = new CancellationTokenSource())
        {
            var active = ConsumeAsync(client, Workload.Query(10000, 64, delayMs: 10), cancel.Token);
            await Task.Delay(40);
            bool busy = false;
            try
            {
                await Workload.ExecuteAsync(client, "tiny");
            }
            catch (PluginCallException e) when (e.Status == "busy") { busy = true; }
            cancel.Cancel();
            try
            {
                await active;
            }
            catch (OperationCanceledException) { }
            Assert(busy, "Single-flight stream admission");
        }
        Workload.Check(await Workload.ExecuteAsync(client, "tiny"), "tiny", tenant);
        _checks++;
    }
}
