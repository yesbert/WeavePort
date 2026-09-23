using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk;
using WeavePort.Sdk.Client;

if (args.Length == 2 && args[0] == "worker")
{
    var app = args[1] == "default" ? new PluginApplication() : new PluginApplication { PluginVersion = args[1] };
    await app.Function<JsonElement, JsonElement>("echo", (input, _, _) => ValueTask.FromResult(input)).RunAsync();
    return;
}
var entryVersion = typeof(Callbacks).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion;
if (PluginApplication.VersionFromAssembly() != entryVersion.Split('+')[0] || PluginApplication.VersionFromAssembly(includeBuildMetadata: true) != entryVersion) throw new Exception("Assembly version selection failed.");
var versionedAssembly = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new System.Reflection.AssemblyName("VersionFixture"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
versionedAssembly.SetCustomAttribute(new System.Reflection.Emit.CustomAttributeBuilder(typeof(System.Reflection.AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!, ["1.2.3-beta+commit"]));
if (PluginApplication.VersionFromAssembly(versionedAssembly) != "1.2.3-beta" || PluginApplication.VersionFromAssembly(versionedAssembly, true) != "1.2.3-beta+commit") throw new Exception("Prerelease or metadata changed.");
var missingVersion = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new System.Reflection.AssemblyName("MissingVersionFixture"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
try { PluginApplication.VersionFromAssembly(missingVersion); throw new Exception("Missing informational metadata guessed."); }
catch (InvalidOperationException) { }
string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException(name);
string root = Required("WP_VERSION_ROOT");
string dotnet = Required("WP_VERSION_DOTNET");
string node = Required("WP_VERSION_NODE");
string python = Path.Combine(root, "python/bin/python");
string self = typeof(Callbacks).Assembly.Location;
await using var host = new PluginHost(options: new WorkerPoolOptions(MaximumPristineWorkers: 0));
int checks = 0;
foreach (string language in new[] { "csharp", "python", "typescript" })
{
    string executable = language == "csharp" ? dotnet : language == "python" ? python : node;
    string original = language == "csharp" ? self : Path.Combine(root, language == "python" ? "worker.py" : "worker.mjs");
    foreach (var pair in new (string Declared, string Expected)[] { ("default", "1"), ("2", "2"), ("2", "1"), ("", "1") })
    {
        string entry = CompatibilityFixture.Resolve(root, language, executable, original, pair.Expected);
        string[] arguments = language == "csharp" ? [entry, "worker", pair.Declared]
            : [entry, pair.Declared];
        var profile = new ProcessProfile(executable, arguments, trustedCode: true,
            workspaceRoot: Path.Combine(root, "workers"), timeout: TimeSpan.FromSeconds(5));
        var session = await host.BindAsync(new PluginContext("sdk-version-tests", "echo", pair.Expected, "default",
            JsonSerializer.SerializeToElement(new { })), profile, new Callbacks(), []);
        await using var client = new LocalPluginClient(session);
        bool mustSucceed = pair.Declared == "default" || pair.Declared == pair.Expected;
        try
        {
            var result = await client.CallAsync("echo", JsonSerializer.SerializeToElement(new { value = "version-check" }));
            if (!mustSucceed || result.GetProperty("value").GetString() != "version-check")
                throw new InvalidDataException("Unexpected version acceptance/result.");
        }
        catch (PluginCallException error) when (!mustSucceed && error.Status is "version-mismatch" or "failed")
        {
            if (error.MayHaveExecuted) throw new Exception("Version failure dispatched work.");
            if (pair.Declared.Length > 0 && (error.VersionMismatch?.Expected != pair.Expected || error.VersionMismatch.Advertised != pair.Declared)) throw new Exception("Version diagnostic lost.");
        }
        Console.WriteLine($"PASS: {language}, declared='{pair.Declared}', expected='{pair.Expected}'");
        checks++;
    }
}
Console.WriteLine($"{checks} packed SDK version checks passed.");

internal sealed class Callbacks : IHostCallbacks
{
    public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("No callback should execute.");
}
