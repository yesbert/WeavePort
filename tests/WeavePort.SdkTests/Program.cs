using System.Diagnostics;
using System.Text.Json;
using WeavePort.Sdk.Client;
using WeavePort.Sdk.Gateway;
using WeavePort.SdkFixture;

if (args.Contains("--crash-stress"))
{
    await CrashStressChecks.RunAsync();
    return;
}

await ClientOperationChecks.RunAsync();

int checks = 0;
var outcomes = new List<object>();
foreach (bool socket in new[] { false, true })
foreach (bool remote in new[] { false, true })
{
    await using var harness = await Harness.CreateAsync(["csharp", "python", "typescript"], remote, socket);
    if (remote)
    {
        await using var invalid = new RemotePluginClient(harness.GatewayAddress!, "invalid-credential");
        bool rejected = false;
        try { await Workload.ExecuteAsync(invalid, "tiny"); }
        catch (PluginCallException e) when (e.Status == "binding-denied") { rejected = true; }
        Assert(rejected, "Gateway authentication");
    }
    for (int lane = 0; lane < harness.Clients.Length; lane++)
    {
        string tenant = "tenant-" + lane;
        IPluginClient client = harness.Clients[lane];
        foreach (string name in Workload.Cases)
        {
            Workload.Check(await Workload.ExecuteAsync(client, name), name, tenant); checks++;
        }
        var nullValue = await client.CallAsync("echo", JsonSerializer.SerializeToElement<object?>(null));
        Assert(nullValue.ValueKind == JsonValueKind.Null, "Null contract");
        string text = "ü🚀<>&\u0000";
        var typed = await client.CallAsync<object, Echo>("echo", new { value = text });
        Assert(typed.Value == text, "Typed Unicode contract");
        int typedRows = 0;
        await foreach (var row in client.StreamAsync<object, TypedRow>("records", new { count = 2, width = 3 }))
            Assert(row.Id == typedRows++ && row.Text == "xxx" && row.Owner == tenant, "Typed stream");
        int received = 0;
        await foreach (var row in client.StreamAsync("records", Workload.Query(41, 37, callbacks: true)))
            Workload.CheckRow(row, received++, 37, tenant);
        Assert(received == 41, "Callbacks across batches");
        await foreach (var unused in client.StreamAsync("records", Workload.Query(0, 0))) throw new InvalidDataException("Expected empty stream.");
        checks++;
        await foreach (var unused in client.StreamAsync("records", Workload.Query(10000, 64, delayMs: 1))) break;
        Workload.Check(await Workload.ExecuteAsync(client, "tiny"), "tiny", tenant); checks++;
        using (var cancel = new CancellationTokenSource(80))
        {
            bool cancelled = false;
            try { await foreach (var unused in client.StreamAsync("records", Workload.Query(10000, 64, delayMs: 10), cancel.Token)) { } }
            catch (OperationCanceledException) { cancelled = true; }
            Assert(cancelled, "Cancellation surfaced");
        }
        Workload.Check(await Workload.ExecuteAsync(client, "callback"), "callback", tenant); checks++;
        foreach (var query in new[] { Workload.Query(100, 32, failAt: 20), Workload.Query(1, 150000) })
        {
            bool failed = false;
            try { await foreach (var unused in client.StreamAsync("records", query)) { } }
            catch (IOException) { failed = true; }
            Assert(failed, "Partial/oversized result fails");
        }
        if (!socket)
        {
            bool limited = false;
            int delivered = 0;
            try { await foreach (var unused in client.StreamAsync("records", Workload.Query(9000, 8192))) delivered++; }
            catch (IOException) { limited = true; }
            Assert(limited && delivered > 0 && delivered < 9000, "Total stream quota with visible partial output");
        }
        bool denied = false;
        try { await client.CallAsync("owner", JsonSerializer.SerializeToElement(new { operation = "forbidden", input = new { tenant = "tenant-other" } })); }
        catch (PluginCallException e) when (e.Status == "denied") { denied = true; }
        Assert(denied, "Callback grant denied");
        Workload.Check(await Workload.ExecuteAsync(client, "callback"), "callback", tenant); checks++;
        using (var cancel = new CancellationTokenSource())
        {
            var active = ConsumeAsync(client, Workload.Query(10000, 64, delayMs: 10), cancel.Token);
            await Task.Delay(40);
            bool busy = false;
            try { await Workload.ExecuteAsync(client, "tiny"); }
            catch (PluginCallException e) when (e.Status == "busy") { busy = true; }
            cancel.Cancel();
            try { await active; } catch (OperationCanceledException) { }
            Assert(busy, "Single-flight stream admission");
        }
        Workload.Check(await Workload.ExecuteAsync(client, "tiny"), "tiny", tenant); checks++;
        bool crashed = false;
        try { await client.CallAsync("crash", JsonSerializer.SerializeToElement(new { })); }
        catch (PluginCallException) { crashed = true; }
        Assert(crashed, "Worker crash surfaced");
        int victim = (lane + 1) % harness.Clients.Length;
        Workload.Check(await Workload.ExecuteAsync(harness.Clients[victim], "callback"), "callback", "tenant-" + victim); checks++;
        Workload.Check(await Workload.ExecuteAsync(client, "tiny"), "tiny", tenant); checks++;
        outcomes.Add(new { remote, socket, language = new[] { "csharp", "python", "typescript" }[lane], passed = true });
    }
}
var pairOutcomes = new List<object>();
foreach (bool socket in new[] { false, true })
foreach (bool remote in new[] { false, true })
foreach (string language in new[] { "csharp", "python", "typescript" })
{
    await using var pair = await Harness.CreateAsync([language, language], remote, socket);
    IPluginClient attacker = pair.Clients[0], victim = pair.Clients[1];
    Workload.Check(await Workload.ExecuteAsync(attacker, "callback"), "callback", "tenant-0"); checks++;
    Workload.Check(await Workload.ExecuteAsync(victim, "callback"), "callback", "tenant-1"); checks++;
    for (int i = 0; i < 2; i++) await ConsumeAsync(victim, Workload.Query(2, 3), default);
    Assert((await victim.CallAsync("state", JsonSerializer.SerializeToElement(new { }))).GetProperty("closed").GetInt32() == 2, "Victim state established");
    bool crashed = false;
    try { await attacker.CallAsync("crash", JsonSerializer.SerializeToElement(new { })); }
    catch (PluginCallException) { crashed = true; }
    Assert(crashed, "Same-artifact attacker crash");
    Workload.Check(await Workload.ExecuteAsync(victim, "callback"), "callback", "tenant-1"); checks++;
    Assert((await victim.CallAsync("state", JsonSerializer.SerializeToElement(new { }))).GetProperty("closed").GetInt32() == 2, "Victim state survives attacker crash");
    using (var cancel = new CancellationTokenSource(80))
    {
        bool cancelled = false;
        try { await ConsumeAsync(attacker, Workload.Query(10000, 64, delayMs: 10), cancel.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Assert(cancelled, "Same-artifact attacker cancellation");
    }
    Workload.Check(await Workload.ExecuteAsync(victim, "callback"), "callback", "tenant-1"); checks++;
    Workload.Check(await Workload.ExecuteAsync(attacker, "tiny"), "tiny", "tenant-0"); checks++;
    pairOutcomes.Add(new { socket, remote, language, passed = true });
}
// A gateway that accepts TCP but never responds must not create an unbounded SDK call.
using (var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0))
{
    listener.Start();
    var endpoint = new Uri("http://127.0.0.1:" + ((System.Net.IPEndPoint)listener.LocalEndpoint).Port);
    await using var client = new RemotePluginClient(endpoint, "synthetic-deadline-test", TimeSpan.FromMilliseconds(100));
    bool cancelled = false;
    var clock = Stopwatch.StartNew();
    try { await Workload.ExecuteAsync(client, "tiny"); }
    catch (OperationCanceledException) { cancelled = true; }
    Assert(cancelled && clock.Elapsed < TimeSpan.FromSeconds(5), "Unresponsive gateway call timeout");
    cancelled = false;
    clock.Restart();
    try { await ConsumeAsync(client, Workload.Query(1, 1), default); }
    catch (OperationCanceledException) { cancelled = true; }
    Assert(cancelled && clock.Elapsed < TimeSpan.FromSeconds(10), "Unresponsive gateway stream and cleanup timeout");
}
await File.WriteAllTextAsync(Path.Combine(Harness.Output, "verification.json"), JsonSerializer.Serialize(new { checks, outcomes, pairOutcomes, passed = true }));
Console.WriteLine($"Passed {checks} SDK checks.");

void Assert(bool condition, string name) { if (!condition) throw new InvalidDataException(name); checks++; }
static async Task ConsumeAsync(IPluginClient client, JsonElement query, CancellationToken token) { await foreach (var unused in client.StreamAsync("records", query, token)) { } }

internal sealed record Echo(string Value);
internal sealed record TypedRow(int Id, string Text, string Owner);
