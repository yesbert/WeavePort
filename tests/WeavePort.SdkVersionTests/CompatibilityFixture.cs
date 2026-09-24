using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using WeavePort.Hosting;

internal static class CompatibilityFixture
{
    internal static string Resolve(string root, string language, string runtime, string original, string version)
    {
        string alias = language == "csharp" ? "dotnet" : language == "typescript" ? "node" : "python";
        string releases = Path.Combine(root, "compatibility", language, "releases");
        string directory = Path.Combine(releases, version);
        Directory.CreateDirectory(directory);
        CopyArtifact(language, original, directory);

        using var policyStream = typeof(PluginHost).Assembly.GetManifestResourceStream("WeavePort.LocalCompatibility")!;
        var policy = JsonNode.Parse(policyStream)!;
        var sdk = policy["AuthorSdks"]![alias]!.DeepClone();
        policy["AuthorSdks"] = new JsonObject { [alias] = sdk };
        var files = Directory.GetFiles(directory).Where(p => Path.GetFileName(p) != "installation.json")
            .ToDictionary(p => Path.GetFileName(p)!, Hash);
        var manifest = new
        {
            Schema = 1,
            Plugin = "sdk-echo",
            Version = version,
            Contract = "echo/v1",
            EntryPoints = new Dictionary<string, string> { [alias] = Path.GetFileName(original) },
            Files = files,
            RuntimeFiles = new Dictionary<string, string> { [alias] = Hash(runtime) },
            Compatibility = policy
        };
        File.WriteAllText(Path.Combine(directory, "installation.json"), JsonSerializer.Serialize(manifest));
        var installed = new InstalledPluginCatalog(releases, new Dictionary<string, string> { [alias] = runtime }).Resolve("sdk-echo", version, "echo/v1");
        return installed.EntryPoints[alias];
    }
    private static void CopyArtifact(string language, string original, string directory)
    {
        if (language != "csharp")
        {
            File.Copy(original, Path.Combine(directory, Path.GetFileName(original)), true);
            return;
        }
        foreach (string file in Directory.GetFiles(Path.GetDirectoryName(original)!))
        {
            File.Copy(file, Path.Combine(directory, Path.GetFileName(file)), true);
        }
    }

    private static string Hash(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input));
    }
}
