using System.Text.Json;
using System.Text.Json.Serialization;
using WeavePort.Hosting;

string root = Path.GetFullPath(args[0]);
string release = Path.Combine(root, "releases", args[2]);
var entries = new Dictionary<string, string> { ["dotnet"] = args[4] };
var external = new Dictionary<string, string>();
if (args.Length > 6)
{
    entries["python"] = "plugin.py";
    foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "python"), "*.py", SearchOption.AllDirectories))
    {
        string relative = Path.GetRelativePath(Path.Combine(root, "python"), path).Replace(Path.DirectorySeparatorChar, '/');
        if (relative.Split('/').Contains("weaveport_sdk")) { external["sdk/" + relative] = path; }
    }
}
var json = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter<WorkerReusePolicy>() } };
string launchPath = Path.Combine(release, "launch.json");
PluginLaunchDeclaration launch = File.Exists(launchPath) ? JsonSerializer.Deserialize<PluginLaunchDeclaration>(File.ReadAllText(launchPath), json)! : new() { Runtime = "dotnet" };
InstallationIdentity identity = PluginInstallationSealer.Seal(new()
{
    ReleaseDirectory = release, Plugin = args[1], Version = args[2], Contract = args[3],
    EntryPoints = entries, ExternalFiles = external, Launch = launch
});
Console.WriteLine($"Sealed {identity.Plugin}/{identity.Version}: {identity.Digest}");
