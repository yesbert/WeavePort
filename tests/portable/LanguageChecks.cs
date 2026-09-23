using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;

internal static class LanguageChecks
{
    internal static async Task RunAsync(string repository, string work, string python, string node)
    {
        foreach (var (alias, executable) in new[] { ("python", python), ("node", node) })
        {
            foreach (WorkerReusePolicy ownership in Enum.GetValues<WorkerReusePolicy>())
            {
                string root = Path.Combine(work, alias + "-" + ownership);
                string release = Path.Combine(root, "1");
                Directory.CreateDirectory(release);
                string sdk = Path.Combine(release, "sdk");
                Directory.CreateDirectory(sdk);
                bool shared = ownership == WorkerReusePolicy.Shared;
                string entry = alias == "python" ? "entry.py" : "entry.mjs";
                if (alias == "python")
                {
                    string package = Directory.CreateDirectory(Path.Combine(sdk, "weaveport_sdk")).FullName;
                    foreach (string file in Directory.GetFiles(Path.Combine(repository, "sdks/python/weaveport_sdk"), "*.py")) File.Copy(file, Path.Combine(package, Path.GetFileName(file)), true);
                    File.WriteAllText(Path.Combine(release, entry), "import sys\nfrom pathlib import Path\nsys.path.insert(0, str(Path(__file__).parent / 'sdk'))\nfrom weaveport_sdk import PluginApplication\napp = PluginApplication('1', concurrent_calls=" + (shared ? "True" : "False") + ")\n@app.function('echo')\nasync def echo(value, context):\n    return value\napp.run()\n");
                    File.WriteAllText(Path.Combine(release, "pyproject.toml"), "[project]\nrequires-python = '>=3.11,<4'\n");
                }
                else
                {
                    foreach (string file in Directory.GetFiles(Path.Combine(repository, "sdks/typescript/dist"), "*.js")) File.Copy(file, Path.Combine(sdk, Path.GetFileName(file)), true);
                    File.WriteAllText(Path.Combine(release, "package.json"), "{\"type\":\"module\",\"engines\":{\"node\":\">=20 <30\"}}");
                    File.WriteAllText(Path.Combine(release, entry), "import { PluginApplication } from './sdk/index.js';\nconst app = new PluginApplication('1', { concurrentCalls: " + (shared ? "true" : "false") + " });\napp.function('echo', async value => value);\nawait app.run();\n");
                }
                var options = new PluginSealOptions { ReleaseDirectory = release, Plugin = "test", Version = "1", Contract = "test", EntryPoints = new Dictionary<string, string> { [alias] = entry }, Launch = new() { Runtime = alias, Ownership = [ownership], MaximumDegree = shared ? 2 : 1 } };
                var identity = PluginInstallationSealer.Seal(options);
                var runtime = new Dictionary<string, string> { [alias] = executable };
                var catalog = new InstalledPluginCatalog(root, runtime);
                var installation = await catalog.ResolveAsync("test", "1", "test", identity);
                await using var host = new PluginHost();
                var approval = new PluginApproval { TrustedCode = true, Ownership = ownership, Degree = shared ? 2 : 1 };
                if (shared)
                {
                    await using var instance = await host.ShareAsync(installation, approval, JsonSerializer.SerializeToElement(new { }), new NoCallbacks(), []);
                    await using var client = instance.For("tenant");
                    await EchoAsync(client);
                }
                else
                {
                    await using var client = await host.BindAsync(installation, approval, new("tenant", JsonSerializer.SerializeToElement(new { })), new NoCallbacks(), []);
                    await EchoAsync(client);
                }
                await host.DisposeAsync();
                PluginInstallationSealer.Seal(options with { StrictRuntimeFiles = runtime });
                _ = await catalog.ResolveAsync("test", "1", "test");
                string declaration = Path.Combine(release, alias == "python" ? "pyproject.toml" : "package.json");
                File.WriteAllText(declaration, alias == "python" ? "[project]\nrequires-python = '>=99'\n" : "{\"type\":\"module\",\"engines\":{\"node\":\">=99\"}}");
                PluginInstallationSealer.Seal(options);
                try { await catalog.ResolveAsync("test", "1", "test"); throw new Exception("Incompatible runtime accepted"); }
                catch (InvalidDataException error) when (error.Message.Contains("requires", StringComparison.Ordinal) && error.Message.Contains("observed", StringComparison.Ordinal)) { }
                Console.WriteLine($"PASS real {alias} {ownership}: portable invocation, strict executable and incompatible version refusal");
            }
        }
    }

    private static async Task EchoAsync(IBoundPluginClient client)
    {
        var answer = await client.CallAsync("echo", JsonSerializer.SerializeToElement(new { value = 42 }));
        if (answer.GetProperty("value").GetInt32() != 42) throw new Exception("Real language call failed");
    }
}
