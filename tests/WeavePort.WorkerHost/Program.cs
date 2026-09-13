using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using WeavePort.Hosting;
using WeavePort.Sdk.Gateway;
using WeavePort.SdkFixture;

Configuration configuration = JsonSerializer.Deserialize<Configuration>(await Console.In.ReadLineAsync() ?? throw new InvalidDataException("Missing configuration."))!;
await using var host = new PluginHost(new FixtureLogger(), options: new WorkerPoolOptions(MaximumPristineWorkers: 0, MaximumConcurrentStarts: configuration.MaximumConcurrentStarts));
var clients = await Fixture.BindAsync(configuration, host);
await using var registry = new GatewayRegistry();
string[] credentials = clients.Select(registry.Register).ToArray();
var builder = WebApplication.CreateSlimBuilder();
builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listener => listener.Protocols = HttpProtocols.Http2));
builder.Services.AddSingleton(registry);
builder.Services.AddGrpc(options => { options.MaxReceiveMessageSize = 1 << 20; options.MaxSendMessageSize = 1 << 20; });
await using var app = builder.Build();
app.MapGrpcService<GatewayService>();
await app.StartAsync();
// Launcher-only configuration/diagnostics: never publish credentials in benchmark evidence.
Console.WriteLine(JsonSerializer.Serialize(new Ready(app.Urls.Single(), credentials, Environment.ProcessId, Fixture.Loaded())));
while (await Console.In.ReadLineAsync() is { } command)
{
    if (command == "quit") break;
    if (command == "warmup")
    {
        foreach (var client in clients)
        {
            await client.CallAsync("echo", JsonSerializer.SerializeToElement(new { }));
        }
        Console.WriteLine(JsonSerializer.Serialize(new { warmed = clients.Length }));
        continue;
    }
    if (command == "diagnostics")
    {
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            snapshot = host.Snapshot,
            workers = Fixture.WorkerIds(clients),
            managedBytes = GC.GetTotalMemory(false),
            allocatedBytes = GC.GetTotalAllocatedBytes(),
            gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2),
            handles = current.HandleCount
        }));
        continue;
    }
    if (command != "snapshot") throw new InvalidDataException("Unknown launcher command.");
    Console.WriteLine(JsonSerializer.Serialize(Fixture.WorkerIds(clients)));
}
await app.StopAsync();

// Only the host's existing safe source-generated events; no exception objects or payloads.
sealed class FixtureLogger : ILogger<PluginHost>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => level >= LogLevel.Warning;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
    {
        if (id.Id is >= 1001 and <= 1006)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { at = TimeProvider.System.GetUtcNow(), eventId = id.Id, message = formatter(state, null) }));
        }
    }
}
