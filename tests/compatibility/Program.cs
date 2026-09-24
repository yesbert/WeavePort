using System.Reflection;
using System.Text.Json.Nodes;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk;
using WeavePort.Sdk.Client;

string root = Path.GetFullPath(args[0]);
string work = Path.Combine(root, "artifacts", "compatibility");
Directory.CreateDirectory(work);
var policy = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "compatibility/local-v1.json")))!;
Assembly[] assemblies = [typeof(PluginContext).Assembly, typeof(PluginHost).Assembly, typeof(IPluginClient).Assembly, typeof(PluginApplication).Assembly];
int checks = 0;
void Check(bool condition, string label)
{
    if (!condition)
    {
        throw new InvalidDataException("FAIL: " + label);
    }

    checks++;
    Console.WriteLine("PASS: " + label);
}
foreach (var assembly in assemblies)
{
    string name = assembly.GetName().Name!;
    string expected = name == "WeavePort.Sdk" ? policy["AuthorSdks"]!["dotnet"]!["Version"]!.GetValue<string>() : policy["HostPackages"]![name]!.GetValue<string>();
    string actual = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
    Check(actual == expected, name + " loaded package version matches matrix");
}
using (var stream = typeof(PluginHost).Assembly.GetManifestResourceStream("WeavePort.LocalCompatibility")!)
{
    Check(JsonNode.DeepEquals(JsonNode.Parse(stream), policy), "packed Hosting embeds the reviewed compatibility matrix");
}
string snapshot = ApiSnapshot.Create(assemblies);
File.WriteAllText(Path.Combine(work, "api-actual.txt"), snapshot);
if (args.Length > 1 && args[1] == "--candidate")
{
    Console.WriteLine("API candidate written for explicit review; baseline was not modified and API check was not passed.");
    return;
}
string baseline = args.Length == 3 && args[1] == "--baseline" ? args[2] : Path.Combine(root, "compatibility/public-api.txt");
Check(File.ReadAllText(baseline) == snapshot,
    "packed public/protected API matches reviewed baseline (see artifacts/compatibility/api-actual.txt on drift)");
Console.WriteLine($"Verification passed: {checks} package/API assertions.");
