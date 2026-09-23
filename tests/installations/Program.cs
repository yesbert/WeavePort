using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;

string root = Path.GetFullPath(args[0]);
string dotnet = args[1];
if (args.Contains("--mixed-only")) { await MixedLaunchChecks.RunAsync(root); return; }
string work = Path.Combine(root, "artifacts", "installation-tests", Guid.NewGuid().ToString("N"));
string releases = Path.Combine(work, "releases");
foreach (string version in new[] { "1", "2" })
{
    string source = Path.Combine(root, "artifacts/document-workshop/releases", version);
    string destination = Path.Combine(releases, version);
    Directory.CreateDirectory(destination);
    foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
}
var catalog = new InstalledPluginCatalog(releases, new Dictionary<string, string> { ["dotnet"] = dotnet });
int checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
    checks++;
    Console.WriteLine("PASS: " + label);
}
void Refused(Action action, string label)
{
    try { action(); }
    catch (InvalidDataException) { Check(true, label); return; }
    throw new InvalidOperationException("Expected refusal: " + label);
}
InstalledPlugin Resolve(string version = "1", InstallationIdentity? pin = null) => catalog.Resolve("document-workshop", version, "document-workshop/v1", pin);
var original = Resolve();
var expandedRuntimes = new InstalledPluginCatalog(releases, new Dictionary<string, string> { ["dotnet"] = dotnet, ["unused-runtime"] = dotnet });
Check(expandedRuntimes.Resolve("document-workshop", "1", "document-workshop/v1").Identity == original.Identity, "declared runtime subset accepts extra operator-approved aliases");
string catalogRoot = Path.Combine(work, "catalog");
string nestedRelease = Path.Combine(catalogRoot, "document-workshop", "releases", "1");
Directory.CreateDirectory(nestedRelease);
foreach (string file in Directory.GetFiles(Path.Combine(releases, "1"))) File.Copy(file, Path.Combine(nestedRelease, Path.GetFileName(file)));
File.WriteAllText(Path.Combine(catalogRoot, "document-workshop", "active.txt"), "1");
var discovery = new InstalledPluginCatalog(catalogRoot, new Dictionary<string, string> { ["dotnet"] = dotnet });
Check(discovery.List("document-workshop/v1").Single().Identity == original.Identity, "contract listing returns verified selected release");
Check(discovery.List("different/v1").Count == 0, "contract listing excludes other contracts");
Check(discovery.Resolve("document-workshop", "1", "document-workshop/v1", original.Identity).Identity == original.Identity, "nested catalog preserves digest pins");
string extra = Path.Combine(releases, "1", "injected.dll");
File.WriteAllText(extra, "extra");
Refused(() => Resolve(), "undeclared bundle dependency rejected");
File.Delete(extra);
File.CreateSymbolicLink(extra, original.EntryPoints["dotnet"]);
Refused(() => Resolve(), "bundle symlink rejected without following it");
File.Delete(extra);
Check(original.EntryPoints.ContainsKey("dotnet"), "packed resolver returns declared verified entry point");
string selector = Path.Combine(work, "active.txt");
catalog.Activate(selector, "document-workshop", "2", "document-workshop/v1");
Check(InstalledPluginCatalog.ReadSelection(selector) == "2" && Resolve("1", original.Identity).Identity == original.Identity, "activation leaves exact pin resolvable");
Refused(() => Resolve("../2"), "path traversal release rejected");
Refused(() => Resolve("3"), "missing release has no fallback");
Refused(() => catalog.Resolve("other", "1", "document-workshop/v1"), "wrong plugin identity rejected");
Refused(() => catalog.Resolve("document-workshop", "1", "document-workshop/v2"), "unsupported domain contract rejected");
string manifest = Path.Combine(releases, "1", "installation.json");
string baseline = File.ReadAllText(manifest);
void Mutate(Action<JsonNode> edit, string label)
{
    var document = JsonNode.Parse(baseline)!;
    edit(document);
    File.WriteAllText(manifest, document.ToJsonString());
    try { Refused(() => Resolve(), label); }
    finally { File.WriteAllText(manifest, baseline); }
}
Mutate(d => d.AsObject().Remove("Compatibility"), "missing compatibility declaration rejected");
Mutate(d => d["Compatibility"]!["HostApi"] = 99, "unsupported host API rejected");
Mutate(d => d["Compatibility"]!["Protocol"] = 99, "unsupported transport protocol rejected");
Mutate(d => d["Compatibility"]!["HostPackages"]!["WeavePort.Hosting"] = "9.0.0", "unsupported host package version rejected");
Mutate(d => d["Compatibility"]!["HostPackages"]!.AsObject().Remove("WeavePort.Sdk.Client"), "incomplete host package set rejected");
Mutate(d => d["Compatibility"]!["HostPackages"]!["Unreviewed.Package"] = "1.0.0", "extra host package declaration rejected");
Mutate(d => d["Compatibility"]!["AuthorSdks"]!["dotnet"]!["Version"] = "9.0.0", "unsupported author SDK version rejected");
Mutate(d => d["Compatibility"]!["AuthorSdks"]!["dotnet"]!["Package"] = "Unreviewed.Sdk", "wrong author SDK package rejected");
Mutate(d => d["Compatibility"]!["AuthorSdks"]!.AsObject().Remove("dotnet"), "missing entry-point SDK declaration rejected");
Mutate(d => d["Compatibility"]!["AuthorSdks"]!["python"] = new JsonObject { ["Package"] = "weaveport-sdk", ["Version"] = "0.2.0" }, "SDK declaration without entry point rejected");
Mutate(d => d["Compatibility"]!["AuthorSdks"]!["dotnet"] = null, "null SDK declaration rejected");
File.WriteAllText(manifest, baseline.Replace("\"HostApi\": 2", "\"HostApi\": 2, \"HostApi\": 2"));
Refused(() => Resolve(), "duplicate compatibility fields rejected");
File.WriteAllText(manifest, baseline);
var incompatible = JsonNode.Parse(baseline)!;
incompatible["Compatibility"]!["Protocol"] = 99;
File.WriteAllText(manifest, incompatible.ToJsonString());
string oldSelector = File.ReadAllText(selector);
Refused(() => catalog.Activate(selector, "document-workshop", "1", "document-workshop/v1"), "incompatible activation rejected");
Check(File.ReadAllText(selector) == oldSelector, "refused compatibility activation preserves existing default");
File.WriteAllText(manifest, baseline);
Mutate(d => d["Launch"] = new JsonObject { ["Runtime"] = "unapproved", ["MemoryMiB"] = 256 }, "launch runtime must be verified");
Mutate(d => d["Launch"] = new JsonObject { ["Runtime"] = "dotnet", ["MemoryMiB"] = 1 }, "launch reservation has a valid minimum");
Mutate(d => d["Launch"] = new JsonObject { ["Runtime"] = "dotnet", ["MaximumDegree"] = 0 }, "launch degree must be positive");
Mutate(d => { d["Launch"] = new JsonObject { ["Runtime"] = "dotnet", ["Ownership"] = new JsonArray("Shared") }; }, "shared launch requires protocol 2");
Mutate(d => { d["Launch"] = new JsonObject { ["Runtime"] = "dotnet", ["Ownership"] = new JsonArray("Shared", "CustomerBound") }; d["Compatibility"]!["Protocol"] = 2; }, "one launch cannot mix serial and concurrent startup modes");
Mutate(d => d["Schema"] = 99, "unsupported manifest schema rejected");
Mutate(d => d["Version"] = "2", "wrong manifest release rejected");
Mutate(d => d["EntryPoints"]!["dotnet"] = "not-declared.dll", "undeclared entry rejected");
Mutate(d => d["Files"]!["../outside.dll"] = new string('A', 64), "bundle traversal rejected");
Mutate(d => d["Runtimes"]!["dotnet"]!["Sha256"] = new string('A', 64), "changed runtime executable rejected");
Mutate(d => d["Runtimes"]!["unexpected"] = d["Runtimes"]!["dotnet"]!.DeepClone(), "unapproved runtime alias rejected");
File.WriteAllText(manifest, baseline.Replace("\"Schema\": 2", "\"Schema\": 2, \"Schema\": 2"));
Refused(() => Resolve(), "duplicate manifest fields rejected");
File.WriteAllText(manifest, baseline + " ");
Refused(() => Resolve("1", original.Identity), "changed manifest cannot replace persisted content identity");
File.WriteAllText(manifest, baseline);
string binary = original.EntryPoints["dotnet"];
byte[] saved = File.ReadAllBytes(binary);
File.WriteAllBytes(binary, [1, 2, 3]);
Refused(() => Resolve(), "modified artifact rejected even for a new operation");
File.Delete(binary);
Refused(() => Resolve(), "missing artifact rejected");
File.WriteAllBytes(binary, saved);
Check(Resolve("1", original.Identity).Identity == original.Identity, "restored approved installation resolves original pin");
// Trusted metadata deliberately describes v1 content as v2 to reach the independent startup guard.
string secondManifest = Path.Combine(releases, "2", "installation.json");
var second = JsonNode.Parse(File.ReadAllText(secondManifest))!;
string secondBinary = Path.Combine(releases, "2", "DocumentWorkshop.Worker.dll");
File.Copy(binary, secondBinary, true);
second["Files"]!["DocumentWorkshop.Worker.dll"] = Convert.ToHexString(SHA256.HashData(saved));
File.WriteAllText(secondManifest, second.ToJsonString());
var wrong = Resolve("2");
await using (var host = new PluginHost(options: new WorkerPoolOptions(MaximumPristineWorkers: 0)))
{
    var session = await host.BindAsync(new PluginContext("test", "markdown", "2", "default", JsonSerializer.SerializeToElement(new { })),
        new ProcessProfile(dotnet, [wrong.EntryPoints["dotnet"], "markdown"], true, Path.Combine(work, "workers")), new Callbacks(), []);
    await using var client = new LocalPluginClient(session);
    try
    {
        await client.CallAsync<object, JsonElement>("reader.describe", new { });
        throw new InvalidOperationException("Expected startup mismatch.");
    }
    catch (PluginCallException error) when (error.Status == "version-mismatch")
    {
        Check(true, "real v1 worker under trusted v2 metadata fails startup version guard");
    }
}
await using (var approvalHost = new PluginHost())
{
    var declared = Resolve();
    async Task RejectApproval(PluginApproval approval, string label)
    {
        try
        {
            await using var unused = await approvalHost.BindAsync(declared, approval,
                new TenantBinding("test", JsonSerializer.SerializeToElement(new { })), new Callbacks(), []);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentOutOfRangeException)
        {
            Check(approvalHost.Snapshot.Workers == 0, label);
            return;
        }
        throw new InvalidOperationException("Expected approval rejection: " + label);
    }
    await RejectApproval(new PluginApproval(), "untrusted approval refused before startup");
    await RejectApproval(new PluginApproval { TrustedCode = true, MaximumCallbacks = 0 }, "callback budget validated before startup");
    await RejectApproval(new PluginApproval { TrustedCode = true, StartupTimeout = TimeSpan.Zero }, "startup deadline validated before startup");
    await RejectApproval(new PluginApproval { TrustedCode = true, MaximumMemoryMiB = 64 }, "memory approval must cover launch reservation");
}
await MixedLaunchChecks.RunAsync(root);
Console.WriteLine($"Verification passed: {checks} assertions. Evidence: {work}");
File.WriteAllText(Path.Combine(work, "result.txt"), $"{checks} assertions passed\n");

internal sealed class Callbacks : IHostCallbacks
{
    public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken) => throw new UnauthorizedAccessException();
}
