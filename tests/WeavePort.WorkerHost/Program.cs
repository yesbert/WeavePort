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
builder.Services.AddGrpc(options => { options.MaxReceiveMessageSize = LauncherCommands.MaximumMessageBytes; options.MaxSendMessageSize = LauncherCommands.MaximumMessageBytes; });
await using var app = builder.Build();
app.MapGrpcService<GatewayService>();
await app.StartAsync();
// Launcher-only configuration/diagnostics: never publish credentials in benchmark evidence.
Console.WriteLine(JsonSerializer.Serialize(new Ready(app.Urls.Single(), credentials, Environment.ProcessId, Fixture.Loaded())));
while (await Console.In.ReadLineAsync() is { } command)
{
    if (command == "quit")
    {
        break;
    }

    if (command == "warmup")
    {
        await LauncherCommands.WarmupAsync(clients);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            warmed = clients.Length
        }));
        continue;
    }
    if (command == "diagnostics")
    {
        LauncherCommands.WriteDiagnostics(host, clients);
        continue;
    }
    if (command != "snapshot")
    {
        throw new InvalidDataException("Unknown launcher command.");
    }

    Console.WriteLine(JsonSerializer.Serialize(Fixture.WorkerIds(clients)));
}
await app.StopAsync();
