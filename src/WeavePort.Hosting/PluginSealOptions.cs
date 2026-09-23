namespace WeavePort.Hosting;
/// <summary>Inputs for sealing an offline built release. Portable sealing never executes the plugin or its runtimes.</summary>
public sealed record PluginSealOptions
{
    /// <summary>Existing release directory. Files must remain stable throughout sealing.</summary>
    public required string ReleaseDirectory { get; init; }
    /// <summary>Installation plugin identifier.</summary>
    public required string Plugin { get; init; }
    /// <summary>Exact artifact release advertised by the worker.</summary>
    public required string Version { get; init; }
    /// <summary>Application-owned contract identifier.</summary>
    public required string Contract { get; init; }
    /// <summary>Runtime aliases (dotnet, python, node) mapped to relative bundle entry points.</summary>
    public required IReadOnlyDictionary<string, string> EntryPoints { get; init; }
    /// <summary>Optional relative declaration paths; defaults to adjacent runtimeconfig.json, pyproject.toml or package.json.</summary>
    public IReadOnlyDictionary<string, string> RuntimeSources { get; init; } = new Dictionary<string, string>();
    /// <summary>Author-declared launch settings, subject to separate operator approval.</summary>
    public required PluginLaunchDeclaration Launch { get; init; }
    /// <summary>Optional approved local executable paths whose hashes must also match at resolution and startup.</summary>
    public IReadOnlyDictionary<string, string> StrictRuntimeFiles { get; init; } = new Dictionary<string, string>();
    /// <summary>Additional external code aliases and local files to hash, supplied again by the deployment when resolving.</summary>
    public IReadOnlyDictionary<string, string> ExternalFiles { get; init; } = new Dictionary<string, string>();
}
