using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk;
using WeavePort.Sdk.Client;

if (args.Contains("--worker"))
{
    // A real provider could load one immutable model here and reuse it across invocations.
    var app = new PluginApplication { ConcurrentCalls = true };
    app.Function<Request, Response>("describe", async (input, context, token) =>
    {
        await Task.Delay(25, token);
        return new Response(context.Tenant, input.Text.ToUpperInvariant());
    });
    await app.RunAsync();
    return;
}

if (args.Length != 2) throw new ArgumentException("Usage: SharedExample <catalog-root> <absolute-dotnet-path>");
var catalog = new InstalledPluginCatalog(args[0], new Dictionary<string, string> { ["dotnet"] = args[1] });
InstalledPlugin installation = catalog.List("shared-example/v1").Single();
await using var host = new PluginHost(new SchedulingOptions
{
    MemoryBudgetMiB = 512,
    MaximumWorkers = 2,
    MaximumPristineWorkers = 0,
    MaximumHeavyCalls = 0
});
var approval = new PluginApproval
{
    TrustedCode = true,
    Ownership = WorkerReusePolicy.Shared,
    MaximumMemoryMiB = 256,
    Degree = 4
};
await using var plugin = await host.ShareAsync(installation, approval, JsonSerializer.SerializeToElement(new { }), new NoCallbacks(), []);
await using var alice = plugin.For("alice");
await using var bob = plugin.For("bob");
Response[] results = await Task.WhenAll(
    alice.CallAsync<Request, Response>("describe", new("hello")),
    bob.CallAsync<Request, Response>("describe", new("world")));
if (results[0].Tenant != "alice" || results[1].Tenant != "bob") throw new InvalidOperationException("Tenant identity mismatch.");
foreach (Response result in results) Console.WriteLine($"{result.Tenant}: {result.Text}");
Console.WriteLine($"Ready shared workers: {plugin.Snapshot.ReadyWorkers}");

internal sealed record Request(string Text);
internal sealed record Response(string Tenant, string Text);
internal sealed class NoCallbacks : IHostCallbacks
{
    public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken) => throw new UnauthorizedAccessException();
}
