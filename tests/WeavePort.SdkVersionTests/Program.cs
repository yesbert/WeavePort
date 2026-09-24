using System.Text.Json;
using WeavePort.Sdk;

if (args.Length == 2 && args[0] == "worker")
{
    var app = args[1] == "default" ? new PluginApplication() : new PluginApplication { PluginVersion = args[1] };
    await app.Function<JsonElement, JsonElement>("echo", (input, _, _) => ValueTask.FromResult(input)).RunAsync();
    return;
}
await VersionChecks.RunAsync();
