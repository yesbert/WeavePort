using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Composition;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;

if (args[0] == "--api") { ApiChecks.Run(args[1], args.Contains("--candidate")); return; }

// Preserve source compatibility with the original nullable-timeout overload.
await using (var compatible = new WeavePort.Sdk.Gateway.RemotePluginClient(new Uri("http://localhost"), "test", null)) { }

await TransportChecks.RunAsync();

string root = Path.GetFullPath(args[0]);
string storage = Path.Combine(root, "artifacts/optional/storage-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(storage);
await CompositionChecks.RunAsync(storage);
await using var host = new PluginHost();
var profile = new ProcessProfile(args[1], [Path.Combine(root, "artifacts/optional/worker/Worker.dll")],
    trustedCode: true, workspaceRoot: Path.Combine(root, "artifacts/optional/workspaces"));
async Task<LocalPluginClient> BindAsync(string tenant) => new(await host.BindAsync(
    new PluginContext(tenant, "optional-fixture", "1", "default", Check.Json(new { })), profile, new Callbacks(), []));
await using (var local = await BindAsync("A"))
{
    await VerifyMappingAsync(local, storage, "local SDK");
}
await using (var tls = new TlsFixture())
{
    await tls.StartAsync();
    string a = tls.Registry.Register(await BindAsync("A"));
    string b = tls.Registry.Register(await BindAsync("B"));
    await using var remote = tls.Client(a);
    await using var other = tls.Client(b);
    Check.That(await remote.GetTenantAsync() == "A" && await other.GetTenantAsync() == "B", "TLS authenticated binding discovery");
    await VerifyMappingAsync(remote, storage, "remote TLS SDK");
    await using (var scope = new ResultScope(storage, "B"))
    {
        var input = await scope.CreateAsync((s, t) => s.WriteAsync(new byte[4096], t).AsTask());
        await Check.Fails<UnauthorizedAccessException>(() => Composition.MapAsync(scope, input, remote), "remote composition rejects another tenant");
        Check.That(scope.ReservedBytes == input.Length, "foreign remote mapping commits no result");
    }
    int count = 0;
    await foreach (var number in remote.StreamAsync("numbers", Check.Json(33))) Check.That(number.GetInt32() == count++, "TLS SDK stream ordering");
    Check.That(count == 33, "TLS SDK complete stream");
    await foreach (var number in remote.StreamAsync("numbers", Check.Json(100))) break;
    Check.That((await remote.CallAsync("echo", Check.Json("after-break"))).GetString() == "after-break", "call after abandoned TLS stream");
    await tls.Registry.RevokeAsync(a);
    await Check.Fails<PluginCallException>(() => remote.GetTenantAsync(), "revoked identity denied on open transport");
    Check.That((await other.CallAsync("echo", Check.Json("B"))).GetString() == "B", "other tenant remains usable after revoke");
    await using var invalid = tls.Client("unknown");
    var denied = await Check.Fails<PluginCallException>(() => invalid.GetTenantAsync(), "unknown credential denied");
    Check.That(!denied.MayHaveExecuted, "unauthorized request cannot execute");
    await using var untrusted = tls.Client(b, trust: false);
    await Check.Fails<PluginCallException>(() => untrusted.CallAsync("echo", Check.Json(1)), "untrusted TLS certificate refused");
    await using var wrongName = tls.Client(b, new UriBuilder(tls.Address) { Host = "127.0.0.1" }.Uri);
    await Check.Fails<PluginCallException>(() => wrongName.CallAsync("echo", Check.Json(1)), "TLS hostname mismatch refused");
    await GatewayChecks.RunAsync(tls);
}
await using (var incompatible = new TlsFixture())
{
    await incompatible.StartAsync(incompatible: true);
    foreach (string variant in new[] { "test", "array", "null", "string-version", "null-version", "missing-tenant", "numeric-tenant", "blank-tenant" })
    {
        await using var client = incompatible.Client(variant);
        await Check.Fails<InvalidDataException>(() => client.GetTenantAsync(), "invalid gateway identity rejected: " + variant);
    }
}
Check.That(!Directory.EnumerateFileSystemEntries(storage).Any(), "local and remote composition cleanup");
Directory.Delete(storage);
await File.WriteAllTextAsync(Path.Combine(root, "artifacts/optional/results.json"), JsonSerializer.Serialize(new
{
    passed = Check.Count, failures = 0, topology = "direct HTTP/2 TLS on loopback with real separate SDK worker processes",
    os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, runtime = Environment.Version.ToString()
}, new JsonSerializerOptions { WriteIndented = true }));

static async Task VerifyMappingAsync(IBoundPluginClient client, string storage, string label)
{
    await using var scope = new ResultScope(storage, "A");
    byte[] expected = Enumerable.Range(0, 1024 * 1024 + 17).Select(i => (byte)(i % 251)).ToArray();
    var input = await scope.CreateAsync((s, t) => s.WriteAsync(expected, t).AsTask());
    var output = await Composition.MapAsync(scope, input, client);
    using var destination = new MemoryStream();
    await scope.CopyToAsync(output, destination);
    Check.That(destination.ToArray().SequenceEqual(expected), label + " complete multi-block composition");
    var fan = await Composition.FanOutAsync(scope, output,
        Enumerable.Range(0, 3).Select<int, Func<ResultScope, ResultHandle, CancellationToken, Task<ResultHandle>>>(_ =>
            (s, h, t) => s.TransformAsync(h, (b, _) => ValueTask.FromResult(b), cancellationToken: t)).ToArray(), 2);
    var merged = await Composition.ConcatenateAsync(scope, fan);
    Check.That(merged.Length == expected.Length * 3L, label + " fan-out and ordered merge");
}

internal sealed class Callbacks : IHostCallbacks
{
    public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken token) => throw new UnauthorizedAccessException();
}
