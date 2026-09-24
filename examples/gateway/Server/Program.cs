using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;
using WeavePort.Sdk.Gateway;

// Configure the HTTPS HTTP/2 listener and certificate through Kestrel configuration.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddWeavePortGateway();
await using var app = builder.Build();
await using var host = new PluginHost();
string Required(string key) => builder.Configuration[key] ?? throw new ArgumentException("Missing configuration: " + key);
var profile = new ProcessProfile(Required("Worker:Dotnet"), [Required("Worker:Assembly")], trustedCode: true);
var context = new PluginContext(Required("Worker:Tenant"), "gateway-example", "1", "default", JsonSerializer.SerializeToElement(new { }));
var client = new LocalPluginClient(await host.BindAsync(context, profile, new Callbacks(), []));
var registry = app.Services.GetRequiredService<GatewayRegistry>();
string credential = registry.Register(client);
// Bootstrap goes only to an explicitly selected private file, never to logs or HTTP.
var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
if (!OperatingSystem.IsWindows())
{
    options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
}

await using (var file = new FileStream(Required("Gateway:CredentialFile"), options))
{
    await JsonSerializer.SerializeAsync(file, new
    {
        credential
    });
}
app.MapWeavePortGateway();
try
{
    await app.RunAsync();
}
finally { await registry.DisposeAsync(); }

internal sealed class Callbacks : IHostCallbacks
{
    public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken token) => throw new UnauthorizedAccessException();
}
