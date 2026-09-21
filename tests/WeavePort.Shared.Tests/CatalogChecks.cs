using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using WeavePort.Abstractions;
using WeavePort.Hosting;

internal static class CatalogChecks
{
    private const string Contract = "catalog-shared/v1";
    internal static async Task RunAsync(string root, string python, string node)
    {
        string work = Path.Combine(root, "artifacts/shared-catalog-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var runtimes = new Dictionary<string, string> { ["python"] = python, ["node"] = node, ["extra-approved"] = node };
        await SealAsync(root, work, "python-shared", "python", python, "1", Contract, true);
        await SealAsync(root, work, "node-shared", "node", node, "1", Contract, true);
        var catalog = new InstalledPluginCatalog(work, runtimes);
        IReadOnlyList<InstalledPlugin> installed = catalog.List(Contract);
        Require(installed.Count == 2, "One contract must discover two independently sealed language installations.");
        Require(catalog.List("other/v1").Count == 0, "Contract filtering failed.");
        await using var host = new PluginHost(new SchedulingOptions { MaximumWorkers = 4, MaximumHeavyCalls = 0, MaximumPristineWorkers = 0, MemoryBudgetMiB = 512 });
        var approval = new PluginApproval { TrustedCode = true, Ownership = WorkerReusePolicy.Shared, Degree = 2, MaximumMemoryMiB = 128 };
        foreach (InstalledPlugin plugin in installed)
        {
            await RejectApprovalAsync(host, plugin, approval with { Ownership = WorkerReusePolicy.CustomerBound }, "CustomerBound approval cannot authorize Shared declaration.");
            await RejectApprovalAsync(host, plugin, approval with { Degree = 3 }, "Degree must not exceed the installation declaration.");
            await using var shared = await host.ShareAsync(plugin, approval, JsonSerializer.SerializeToElement(new { }), new NoCallbacks(), []);
            await using var client = shared.For("catalog-tenant");
            JsonElement result = await client.CallAsync("echo", JsonSerializer.SerializeToElement(new { origin = plugin.Identity.Plugin }));
            Require(result.GetProperty("tenant").GetString() == "catalog-tenant", "Installed binding lost tenant identity.");
            Require(result.GetProperty("value").GetProperty("origin").GetString() == plugin.Identity.Plugin, "Installed launch used the wrong entry point.");
            Require(catalog.Resolve(plugin.Identity.Plugin, "1", Contract, plugin.Identity).Identity == plugin.Identity, "Exact manifest pin changed.");
        }
        await SealAsync(root, work, "python-exclusive", "python", python, "1", "catalog-serial/v1", false);
        var exclusive = catalog.Resolve("python-exclusive", "1", "catalog-serial/v1");
        await using (var client = await host.BindAsync(exclusive, new PluginApproval { TrustedCode = true, MaximumMemoryMiB = 128 }, new TenantBinding("serial-tenant", JsonSerializer.SerializeToElement(new { })), new NoCallbacks(), []))
        {
            Require((await client.CallAsync("echo", JsonSerializer.SerializeToElement(new { }))).GetProperty("tenant").GetString() == "serial-tenant", "Installed serial launch failed.");
        }
        CheckRuntimeTampering(catalog, work);
        await SealAsync(root, work, "python-mismatch", "python", python, "2", "mismatch/v1", true);
        InstalledPlugin mismatch = catalog.Resolve("python-mismatch", "2", "mismatch/v1");
        bool refused = false;
        try { await using var unexpected = await host.ShareAsync(mismatch, approval, JsonSerializer.SerializeToElement(new { }), new NoCallbacks(), []); }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException) { refused = true; }
        Require(refused, "Manifest version 2 must reject a worker advertising version 1.");
        Console.WriteLine("PASS: sealed mixed-language catalog, runtime subsets, launch approval, serial binding and independent ready-version guard.");
    }

    private static async Task RejectApprovalAsync(PluginHost host, InstalledPlugin plugin, PluginApproval approval, string label)
    {
        long started = host.Snapshot.WorkersStarted;
        try { await using var unexpected = await host.ShareAsync(plugin, approval, JsonSerializer.SerializeToElement(new { }), new NoCallbacks(), []); }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException)
        {
            Require(host.Snapshot.WorkersStarted == started, "Rejected approval started a process.");
            return;
        }
        throw new InvalidOperationException(label);
    }

    private static void CheckRuntimeTampering(InstalledPluginCatalog catalog, string work)
    {
        string path = Path.Combine(work, "python-shared/releases/1/installation.json");
        string original = File.ReadAllText(path);
        var document = JsonNode.Parse(original)!;
        document["RuntimeFiles"]!["python"] = new string('0', 64);
        File.WriteAllText(path, document.ToJsonString());
        bool refused = false;
        try { catalog.Resolve("python-shared", "1", Contract); }
        catch (InvalidDataException) { refused = true; }
        finally { File.WriteAllText(path, original); }
        Require(refused, "Every selected runtime digest must still be checked when runtime aliases are a subset.");
    }

    private static async Task SealAsync(string root, string work, string plugin, string runtime, string executable, string version, string contract, bool shared)
    {
        string bundle = Path.Combine(work, plugin, "releases", version);
        string sdk = Path.Combine(bundle, "sdk");
        Directory.CreateDirectory(sdk);
        string entry = runtime == "python" ? "entry.py" : "entry.mjs";
        File.Copy(Path.Combine(root, "tests/WeavePort.Shared.Tests/fixtures", runtime == "python" ? "shared.py" : "shared.mjs"), Path.Combine(bundle, entry));
        var arguments = new List<string>();
        if (runtime == "python")
        {
            string? selected = Environment.GetEnvironmentVariable("WP_SHARED_PYTHON_SDK");
            string package = selected == "-" ? await InstalledPythonPackageAsync(executable) : Path.Combine(selected ?? Path.Combine(root, "sdks/python"), "weaveport_sdk");
            string destination = Path.Combine(sdk, "weaveport_sdk");
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.EnumerateFiles(package, "*.py")) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            arguments.AddRange(["--sdk-path", sdk]);
        }
        else
        {
            string package = Path.GetDirectoryName(Environment.GetEnvironmentVariable("WP_SHARED_NODE_SDK") ?? Path.Combine(root, "sdks/typescript/dist/index.js"))!;
            foreach (string file in Directory.EnumerateFiles(package, "*.js")) File.Copy(file, Path.Combine(sdk, Path.GetFileName(file)));
            File.WriteAllText(Path.Combine(sdk, "package.json"), "{\"type\":\"module\"}");
            arguments.AddRange(["--sdk-path", Path.Combine(sdk, "index.js")]);
        }
        if (!shared) arguments.Add("--exclusive");
        var matrix = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "compatibility/local-v1.json")))!;
        var compatibility = new JsonObject { ["HostApi"] = matrix["HostApi"]!.DeepClone(), ["Protocol"] = shared ? 2 : 1, ["HostPackages"] = matrix["HostPackages"]!.DeepClone(), ["AuthorSdks"] = new JsonObject { [runtime] = matrix["AuthorSdks"]![runtime]!.DeepClone() } };
        var files = Directory.EnumerateFiles(bundle, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(bundle, path).Replace(Path.DirectorySeparatorChar, '/'), Hash);
        var manifest = new { Schema = 1, Plugin = plugin, Version = version, Contract = contract, EntryPoints = new Dictionary<string, string> { [runtime] = entry }, Files = files, RuntimeFiles = new Dictionary<string, string> { [runtime] = Hash(executable) }, Compatibility = compatibility, Launch = new { Runtime = runtime, Arguments = arguments, MemoryMiB = 128, Ownership = new[] { shared ? "Shared" : "CustomerBound" }, MaximumDegree = shared ? 2 : 1 } };
        File.WriteAllText(Path.Combine(bundle, "installation.json"), JsonSerializer.Serialize(manifest));
        File.WriteAllText(Path.Combine(work, plugin, "active.txt"), version);
    }

    private static async Task<string> InstalledPythonPackageAsync(string executable)
    {
        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("import pathlib,weaveport_sdk; print(pathlib.Path(weaveport_sdk.__file__).parent)");
        using var process = Process.Start(start)!;
        string path = (await process.StandardOutput.ReadToEndAsync()).Trim();
        await process.WaitForExitAsync();
        Require(process.ExitCode == 0 && Directory.Exists(path), "Installed Python SDK could not be resolved.");
        return path;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class NoCallbacks : IHostCallbacks
    {
        public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken token) => throw new InvalidOperationException("Catalog fixture has no callback grants.");
    }
}
