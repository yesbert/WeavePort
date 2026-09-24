using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;

internal static class MixedLaunchChecks
{
    public static async Task RunAsync(string repository)
    {
        string root = Path.Combine(repository, "artifacts", "installation-tests", "mixed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var runtimes = new Dictionary<string, string>
        {
            ["python"] = Executable("python3"),
            ["node"] = Executable("node")
        };
        var pythonFiles = Directory.GetFiles(Path.Combine(repository, "sdks/python/weaveport_sdk"), "*.py");
        var nodeFiles = Directory.GetFiles(Path.Combine(repository, "sdks/typescript/dist"), "*.js", SearchOption.AllDirectories);
        foreach (string path in pythonFiles.Concat(nodeFiles))
        {
            runtimes["sdk/" + Path.GetRelativePath(repository, path).Replace('\\', '/')] = path;
        }

        Seal(repository, root, new("python-provider", "python", "plugin.py", [Path.Combine(repository, "sdks/python")]), runtimes);
        Seal(repository, root, new("node-provider", "node", "plugin.mjs", [Path.Combine(repository, "sdks/typescript/dist/index.js")]), runtimes);
        var catalog = new InstalledPluginCatalog(root, runtimes);
        var installations = catalog.List("mixed-launch/v1");
        if (installations.Count != 2)
        {
            throw new InvalidOperationException("Mixed root discovery failed.");
        }

        await using var host = new PluginHost(new SchedulingOptions { MaximumWorkers = 2, MemoryBudgetMiB = 256, MaximumHeavyCalls = 0, MaximumPristineWorkers = 0 });
        var approval = new PluginApproval { TrustedCode = true, Ownership = WorkerReusePolicy.Shared, MaximumMemoryMiB = 128, Degree = 2 };
        foreach (InstalledPlugin installation in installations)
        {
            await RejectUnapprovedOwnershipAsync(host, installation, approval);
            await using var shared = await host.ShareAsync(installation, approval, Json(), new NoCallbacks(), []);
            await using var first = shared.For("tenant-a");
            await using var second = shared.For("tenant-b");
            JsonElement[] replies = await Task.WhenAll(first.CallAsync("who", Json()), second.CallAsync("who", Json()));
            if (replies[0].GetProperty("tenant").GetString() != "tenant-a" || replies[1].GetProperty("tenant").GetString() != "tenant-b")
            {
                throw new InvalidOperationException("Mixed catalog client identity failed.");
            }
        }
        Console.WriteLine("PASS: mixed Python/Node verified catalog -> approval -> Shared clients; absent approval refused before launch");
    }

    private static async Task RejectUnapprovedOwnershipAsync(PluginHost host, InstalledPlugin installation, PluginApproval approval)
    {
        long before = host.Snapshot.WorkersStarted;
        try
        {
            await using var denied = await host.ShareAsync(installation, approval with
            {
                Ownership = WorkerReusePolicy.CustomerBound
            }, Json(), new NoCallbacks(), []);
        }
        catch (InvalidOperationException)
        {
            if (host.Snapshot.WorkersStarted != before)
            {
                throw new InvalidOperationException("Rejected approval started a worker.");
            }
            return;
        }
        throw new InvalidOperationException("Missing Shared approval was accepted.");
    }

    private sealed record Provider(string Plugin, string Runtime, string Entry, string[] Arguments);

    private static void Seal(string repository, string root, Provider provider, Dictionary<string, string> runtimes)
    {
        var (plugin, runtime, entry, arguments) = provider;
        string code = File.ReadAllText(Path.Combine(repository, "tests/installations/fixtures", entry));
        string pluginRoot = Path.Combine(root, plugin);
        string release = Path.Combine(pluginRoot, "releases", "1");
        Directory.CreateDirectory(release);
        File.WriteAllText(Path.Combine(release, entry), code);
        var compatibility = JsonNode.Parse(File.ReadAllText(Path.Combine(repository, "compatibility/local-v1.json")))!;
        compatibility.AsObject().Remove("Protocols");
        compatibility["Protocol"] = 2;
        var sdk = compatibility["AuthorSdks"]![runtime]!.DeepClone();
        compatibility["AuthorSdks"] = new JsonObject { [runtime] = sdk };
        var runtimeSubset = runtimes.Where(pair => pair.Key == runtime || pair.Key.Contains(runtime == "python" ? "sdks/python/" : "sdks/typescript/"));
        var manifest = new
        {
            Schema = 1,
            Plugin = plugin,
            Version = "1",
            Contract = "mixed-launch/v1",
            EntryPoints = new Dictionary<string, string> { [runtime] = entry },
            Files = new Dictionary<string, string> { [entry] = Digest(Path.Combine(release, entry)) },
            RuntimeFiles = runtimeSubset.ToDictionary(pair => pair.Key, pair => Digest(pair.Value)),
            Compatibility = compatibility,
            Launch = new
            {
                Runtime = runtime,
                Arguments = arguments,
                MemoryMiB = 128,
                Ownership = new[] { "Shared" },
                MaximumDegree = 2
            }
        };
        File.WriteAllText(Path.Combine(release, "installation.json"), JsonSerializer.Serialize(manifest));
        File.WriteAllText(Path.Combine(pluginRoot, "active.txt"), "1");
    }
    private static string Digest(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }
    private static string Executable(string name) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(path => Path.Combine(path, name)).First(File.Exists);
    private static JsonElement Json() => JsonSerializer.SerializeToElement(new { });
    private sealed class NoCallbacks : IHostCallbacks
    {
        public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken) => throw new UnauthorizedAccessException();
    }
}
