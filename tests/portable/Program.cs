using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk;
using WeavePort.Sdk.Client;

if (args.Contains("--worker"))
{
    var app = new PluginApplication { PluginVersion = "1.0.0" };
    app.Function<JsonElement, JsonElement>("echo", (value, _, _) => ValueTask.FromResult(value));
    await app.RunAsync();
    return;
}
if (args[0] == "languages")
{
    await LanguageChecks.RunAsync(args[1], args[2], args[3], args[4]);
    await StartupChecks.RunAsync(args[2], args[4]);
    return;
}
string root = Path.GetFullPath(args[1]);
string release = Path.Combine(root, "portable", "releases", "1.0.0");
if (args[0] == "seal")
{
    Directory.CreateDirectory(release);
    foreach (string path in Directory.EnumerateFiles(AppContext.BaseDirectory)) File.Copy(path, Path.Combine(release, Path.GetFileName(path)), true);
    var identity = PluginInstallationSealer.Seal(new()
    {
        ReleaseDirectory = release, Plugin = "portable", Version = "1.0.0", Contract = "portable/v1",
        EntryPoints = new Dictionary<string, string> { ["dotnet"] = "Portable.dll" },
        Launch = new() { Runtime = "dotnet", Arguments = ["--worker"] }
    });
    File.WriteAllText(Path.Combine(root, "portable", "active.txt"), "1.0.0");
    File.WriteAllText(Path.Combine(root, "pin.json"), JsonSerializer.Serialize(identity));
    Console.WriteLine(JsonSerializer.Serialize(new { operation = "sealed", identity }));
    return;
}
string executable = args.Length > 2 ? args[2] : Environment.ProcessPath!;
var pin = JsonSerializer.Deserialize<InstallationIdentity>(File.ReadAllText(Path.Combine(root, "pin.json")))!;
var catalog = new InstalledPluginCatalog(root, new Dictionary<string, string> { ["dotnet"] = executable });
var clock = Stopwatch.StartNew();
InstalledPlugin installation = await catalog.ResolveAsync("portable", "1.0.0", "portable/v1", pin);
double resolveMs = clock.Elapsed.TotalMilliseconds;
await using var host = new PluginHost();
clock.Restart();
await using IBoundPluginClient client = await host.BindAsync(installation, new() { TrustedCode = true }, new("tenant", JsonSerializer.SerializeToElement(new { })), new NoCallbacks(), []);
JsonElement answer = await client.CallAsync("echo", JsonSerializer.SerializeToElement(new { message = "portable" }));
if (answer.GetProperty("message").GetString() != "portable") throw new Exception("Echo failed");
using var runtime = File.OpenRead(executable);
Console.WriteLine(JsonSerializer.Serialize(new { operation = "executed", os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, identity = installation.Identity, runtimeSha256 = Convert.ToHexString(SHA256.HashData(runtime)), resolveMs, startAndCallMs = clock.Elapsed.TotalMilliseconds }));

internal sealed class NoCallbacks : IHostCallbacks
{
    public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken) => throw new UnauthorizedAccessException();
}
