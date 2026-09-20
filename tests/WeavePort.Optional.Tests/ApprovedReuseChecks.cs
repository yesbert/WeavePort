using WeavePort.Abstractions;
using WeavePort.Composition;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;

internal static class ApprovedReuseChecks
{
    internal static async Task RunAsync(string root, string dotnet, string storage)
    {
        await using var host = new PluginHost(options: new WorkerPoolOptions(MaximumWorkers: 2, MemoryBudgetMiB: 128, MaximumPristineWorkers: 0));
        var profile = new ProcessProfile(dotnet, [Path.Combine(root, "artifacts/optional/worker/Worker.dll")], true, reservedMemoryMiB: 64)
        {
            ReusePolicy = WorkerReusePolicy.ApprovedSessions
        };
        async Task<LocalPluginClient> BindAsync(string tenant) => new(await host.BindAsync(
            new PluginContext(tenant, "approved-gateway", "1", "default", Check.Json(new { })), profile, new Callbacks(), []));
        await using var tls = new TlsFixture();
        await tls.StartAsync();
        string a = tls.Registry.Register(await BindAsync("A"));
        string b = tls.Registry.Register(await BindAsync("B"));
        await using var first = tls.Client(a);
        await using var second = tls.Client(b);
        Check.That(await first.GetTenantAsync() == "A" && await second.GetTenantAsync() == "B", "approved gateway discovers immutable binding identities");
        Check.That(host.Snapshot.WorkersStarted == 0, "discovery does not start an approved worker");
        for (int i = 0; i < 6; i++)
        {
            var client = i % 2 == 0 ? first : second;
            string tenant = i % 2 == 0 ? "A" : "B";
            Check.That((await client.CallAsync("echo", Check.Json(tenant))).GetString() == tenant, "alternating approved gateway calls retain payload ownership");
            Check.That(await client.GetTenantAsync() == tenant, "approved reuse does not change discovered binding identity");
        }
        Check.That(host.Snapshot.WorkersStarted == 1 && host.Snapshot.ReuseHits >= 5, "gateway customers share one cleaned worker sequentially");
        foreach (var (client, tenant) in new[] { (first, "A"), (second, "B") })
        {
            await using var scope = new ResultScope(storage, tenant);
            byte[] data = Enumerable.Repeat((byte)tenant[0], 8193).ToArray();
            var input = await scope.CreateAsync((s, t) => s.WriteAsync(data, t).AsTask());
            var output = await Composition.MapAsync(scope, input, client, 4096);
            using var destination = new MemoryStream();
            await scope.CopyToAsync(output, destination);
            Check.That(destination.ToArray().SequenceEqual(data), "approved gateway composition preserves customer result bytes");
            await Check.Fails<UnauthorizedAccessException>(() => Composition.MapAsync(scope, input, tenant == "A" ? second : first), "approved composition rejects foreign binding");
        }
        Check.That(host.Snapshot.WorkersStarted == 1, "multi-chunk composition retains approved pooling");
        await using (var stream = first.StreamAsync("numbers", Check.Json(100)).GetAsyncEnumerator())
        {
            Check.That(await stream.MoveNextAsync() && stream.Current.GetInt32() == 0, "approved gateway stream opens");
            Check.That((await second.CallAsync("echo", Check.Json("B"))).GetString() == "B", "another customer runs while stream is pinned");
            Check.That(host.Snapshot.WorkersStarted == 2, "active gateway stream cannot transfer its worker");
        }
        await tls.Registry.RevokeAsync(a);
        Check.That((await second.CallAsync("echo", Check.Json("after-revoke"))).GetString() == "after-revoke", "revoking an old gateway binding preserves another customer's reusable worker");
    }
}
