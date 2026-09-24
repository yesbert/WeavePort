using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using WeavePort.Hosting;

internal static class ManifestChecks
{
    internal static void Run()
    {
        string root = Directory.CreateTempSubdirectory("wp-manifest-").FullName;
        try
        {
            Verify(root);
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("PASS manifest parsing, identity, compatibility and pinned file validation");
    }

    private static void Verify(string root)
    {
        string release = Path.Combine(root, "1");
        Directory.CreateDirectory(release);
        string path = Path.Combine(release, "installation.json");
        var catalog = new InstalledPluginCatalog(root, new Dictionary<string, string>());
        void Reject()
        {
            try
            {
                catalog.Resolve("plugin", "1", "contract");
            }
            catch (InvalidDataException) { return; }
            throw new Exception("Invalid manifest accepted");
        }
        Reject();
        foreach (string content in new[] { new string(' ', 1024 * 1024 + 1), "{", "null", "{\"Schema\":1,\"Schema\":1}" })
        {
            File.WriteAllText(path, content);
            Reject();
        }
        string worker = Path.Combine(release, "worker.dll");
        var manifest = CreateManifest(worker);
        File.WriteAllText(path, manifest.ToJsonString());
        InstalledPlugin original = catalog.Resolve("plugin", "1", "contract");
        if (original.EntryPoints["dotnet"] != worker || catalog.Resolve("plugin", "1", "contract", original.Identity).Identity != original.Identity)
        {
            throw new Exception("Valid manifest identity or entry changed");
        }

        foreach (string field in new[] { "Schema", "Plugin", "Version", "Contract", "EntryPoints", "Files", "RuntimeFiles", "Compatibility" })
        {
            var changed = manifest.DeepClone().AsObject();
            changed.Remove(field);
            File.WriteAllText(path, changed.ToJsonString());
            Reject();
        }
        File.WriteAllText(path, manifest.ToJsonString());
        File.WriteAllText(worker, "mutated");
        Reject();
        File.WriteAllText(worker, "not executed");
        string extra = Path.Combine(release, "unexpected");
        File.WriteAllText(extra, "extra");
        Reject();
        File.Delete(extra);
        if (!OperatingSystem.IsWindows())
        {
            string linked = Path.Combine(root, "linked-manifest");
            File.Move(path, linked);
            File.CreateSymbolicLink(path, linked);
            Reject();
            File.Delete(path);
        }
    }
    private static JsonObject CreateManifest(string worker)
    {
        using var matrix = typeof(PluginHost).Assembly.GetManifestResourceStream("WeavePort.LocalCompatibility")!;
        JsonObject compatibility = JsonNode.Parse(matrix)!.AsObject();
        JsonNode sdk = compatibility["AuthorSdks"]!["dotnet"]!.DeepClone();
        compatibility["AuthorSdks"] = new JsonObject { ["dotnet"] = sdk };
        File.WriteAllText(worker, "not executed");
        string digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(worker)));
        return new JsonObject
        {
            ["Schema"] = 1,
            ["Plugin"] = "plugin",
            ["Version"] = "1",
            ["Contract"] = "contract",
            ["EntryPoints"] = new JsonObject { ["dotnet"] = "worker.dll" },
            ["Files"] = new JsonObject { ["worker.dll"] = digest },
            ["RuntimeFiles"] = new JsonObject(),
            ["Compatibility"] = compatibility
        };
    }

}
