using WeavePort.Hosting;

namespace DecisionRoom.Host;
internal sealed record RuntimePaths(string Root, string Dotnet, string Python, string? Selector = null, string? ReleaseDirectory = null)
{
    internal string SelectorPath => Selector ?? Path.Combine(Root, "active-version.txt");

    internal string ReleaseRoot(string version)
    {
        ValidateVersion(version);
        return Path.Combine(ReleaseDirectory ?? Path.Combine(Root, "releases"), version);
    }

    internal string Plugin(string version) => Path.Combine(ReleaseRoot(version), "DecisionRoom.Plugin.dll");
    internal string PythonPlugin(string version) => Path.Combine(ReleaseRoot(version), "plugin.py");
    internal string CurrentVersion() => ValidateVersion(InstalledPluginCatalog.ReadSelection(SelectorPath));
    internal static string ValidateVersion(string version) => version is "1" or "2" ? version : throw new InvalidDataException("Unsupported Decision Room plugin version; choose 1 or 2.");
    internal InstalledPluginCatalog Catalog()
    {
        var runtimes = new Dictionary<string, string>
        {
            ["dotnet"] = Dotnet,
            ["python"] = Python
        };
        foreach (var path in Directory.GetFiles(Path.Combine(Root, "python"), "*.py", SearchOption.AllDirectories).Where(p => p.Contains("/weaveport_sdk/", StringComparison.Ordinal)))
        {
            runtimes.Add("sdk/" + Path.GetRelativePath(Path.Combine(Root, "python"), path), path);
        }

        return new InstalledPluginCatalog(ReleaseDirectory ?? Path.Combine(Root, "releases"), runtimes);
    }

    internal Dictionary<string, string> Identity(string version)
    {
        var installed = Catalog().Resolve("decision-room", ValidateVersion(version), "decision-room/v1");
        var result = new Dictionary<string, string>
        {
            ["installation"] = installed.Identity.Digest
        };
        foreach (var file in Directory.GetFiles(Path.Combine(Root, "host"), "*.dll").Order(StringComparer.Ordinal))
        {
            result.Add(Path.GetRelativePath(Root, file), Wire.Hash(file));
        }

        return result;
    }

    internal void Activate(string version) => Catalog().Activate(SelectorPath, "decision-room", ValidateVersion(version), "decision-room/v1");
}
