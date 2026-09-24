using System.Text.Json;
using System.Text.Json.Serialization;
using WeavePort.Hosting;

if (args.Length is not (6 or 7))
{
    throw new ArgumentException("Usage: WeavePort.Seal <root> <plugin> <version> <contract> <dotnet-entry> <dotnet-path> [python-path]");
}

// Runtime executable arguments are retained for script compatibility. Portable
// manifests declare runtime requirements; they do not pin these machine paths.
string root = Path.GetFullPath(args[0]);
string release = Path.Combine(root, "releases", args[2]);
var entries = new Dictionary<string, string> { ["dotnet"] = args[4] };
var external = new Dictionary<string, string>();
if (args.Length == 7)
{
    entries["python"] = "plugin.py";
    external = PythonSdkFiles(Path.Combine(root, "python"));
}
var json = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter<WorkerReusePolicy>() } };
string launchPath = Path.Combine(release, "launch.json");
PluginLaunchDeclaration launch = File.Exists(launchPath) ? JsonSerializer.Deserialize<PluginLaunchDeclaration>(File.ReadAllText(launchPath), json)! : new() { Runtime = "dotnet" };
InstallationIdentity identity = PluginInstallationSealer.Seal(new()
{
    ReleaseDirectory = release,
    Plugin = args[1],
    Version = args[2],
    Contract = args[3],
    EntryPoints = entries,
    ExternalFiles = external,
    Launch = launch
});
Console.WriteLine($"Sealed {identity.Plugin}/{identity.Version}: {identity.Digest}");

static Dictionary<string, string> PythonSdkFiles(string environmentRoot)
{
    var files = new Dictionary<string, string>();
    foreach (string path in Directory.EnumerateFiles(environmentRoot, "*.py", SearchOption.AllDirectories))
    {
        string relative = Path.GetRelativePath(environmentRoot, path).Replace(Path.DirectorySeparatorChar, '/');
        if (!relative.Split('/').Contains("weaveport_sdk"))
        {
            continue;
        }

        files["sdk/" + relative] = path;
    }

    return files;
}
