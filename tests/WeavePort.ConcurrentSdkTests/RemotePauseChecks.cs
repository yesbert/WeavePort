using System.Text.Json;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;
using WeavePort.Sdk.Gateway;

internal static class RemotePauseChecks
{
    internal static async Task RunAsync(PluginHost host, RemotePluginClient client, Uri endpoint, string credential)
    {
        using (var stop = new CancellationTokenSource())
        {
            var source = client.SourceAsync("large", JsonSerializer.SerializeToElement(new
            {
                bytes = 200L * 1024 * 1024
            }), cancellationToken: stop.Token).GetAsyncEnumerator();
            if (!await source.MoveNextAsync())
            {
                throw new InvalidOperationException("Source did not begin.");
            }

            stop.Cancel();
            await ReleasedAsync(host);
            int pid = await PidAsync(client);
            await source.DisposeAsync();
            if (await PidAsync(client) != pid)
            {
                throw new InvalidOperationException("Old remote source disposal disrupted new worker.");
            }
        }

        using (var stop = new CancellationTokenSource())
        {
            var stream = client.StreamAsync("slow", JsonSerializer.SerializeToElement(new
            {
                nextDelay = 10000
            }), stop.Token).GetAsyncEnumerator();
            if (!await stream.MoveNextAsync())
            {
                throw new InvalidOperationException("Stream did not begin.");
            }

            stop.Cancel();
            await ReleasedAsync(host);
            int pid = await PidAsync(client);
            await stream.DisposeAsync();
            if (await PidAsync(client) != pid)
            {
                throw new InvalidOperationException("Old remote stream disposal disrupted new worker.");
            }
        }

        await using var timed = new RemotePluginClient(endpoint, credential, streamOptions: new PluginStreamOptions { TotalTimeout = TimeSpan.FromMilliseconds(250) });
        var expired = timed.StreamAsync("slow", JsonSerializer.SerializeToElement(new
        {
            nextDelay = 10000
        })).GetAsyncEnumerator();
        if (!await expired.MoveNextAsync())
        {
            throw new InvalidOperationException("Timed stream did not begin.");
        }

        await ReleasedAsync(host);
        int current = await PidAsync(client);
        await expired.DisposeAsync();
        if (await PidAsync(client) != current)
        {
            throw new InvalidOperationException("Expired remote stream disposal disrupted new worker.");
        }
    }

    private static async Task<int> PidAsync(RemotePluginClient client) => (await client.CallAsync("stats", JsonSerializer.SerializeToElement(new { }))).GetProperty("pid").GetInt32();
    private static async Task ReleasedAsync(PluginHost host)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (host.Scheduling!.Active != 0)
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
