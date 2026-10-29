using System.Security.Cryptography;
using System.Text.Json;
using WeavePort.Sdk;

if (args.Contains("--publisher-lifecycle"))
{
    AppContext.SetSwitch("System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault", true);
    await PublisherLifecycleChecks.RunAsync();
    return;
}

if (args.Contains("--publisher-worker"))
{
    // Only the author fixture enables reflection; the host verification process never does.
    AppContext.SetSwitch("System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault", true);
    var app = new PluginApplication { PluginVersion = "1" };
    app.Function<JsonElement, JsonElement>("echo", (value, _, _) => ValueTask.FromResult(value));
    await app.RunAsync();
    return;
}

if (JsonSerializer.IsReflectionEnabledByDefault)
{
    throw new Exception("Publisher tests require JSON reflection disabled.");
}

using RSA first = RSA.Create(3072);
using RSA second = RSA.Create(4096);
using RSA unknown = RSA.Create(3072);
PublisherChecks.Run(first, second, unknown);
await CatalogChecks.RunAsync(first, second, unknown);
await PublisherLifecycleChecks.RunChildAsync();
InteropChecks.Run();
ExampleChecks.Run(first);
Console.WriteLine("PASS publisher signing, offline verification, catalog policy, rotation, lifecycle and independent interoperability");
