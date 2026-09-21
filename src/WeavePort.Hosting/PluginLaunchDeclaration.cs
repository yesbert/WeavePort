namespace WeavePort.Hosting;
/// <summary>Verified launch requirements declared by the installation author. Operator approval is still required.</summary>
public sealed record PluginLaunchDeclaration
{
    /// <summary>Entry point and approved runtime alias, for example dotnet or python.</summary>
    public required string Runtime { get; init; }
    /// <summary>Literal arguments following the verified entry-point path.</summary>
    public string[] Arguments { get; init; } = [];
    /// <summary>Requested worker memory reservation; not an operating-system limit.</summary>
    public int MemoryMiB { get; init; } = 256;
    /// <summary>Supported ownership policies, explicitly approved by the operator at binding.</summary>
    public WorkerReusePolicy[] Ownership { get; init; } = [WorkerReusePolicy.CustomerBound];
    /// <summary>Maximum supported concurrent invocations per shared worker.</summary>
    public int MaximumDegree { get; init; } = 1;

    internal void Validate(IReadOnlyDictionary<string, string> entries, IReadOnlyDictionary<string, string> runtimes)
    {
        if (string.IsNullOrWhiteSpace(Runtime) || !entries.ContainsKey(Runtime) || !runtimes.ContainsKey(Runtime) || MemoryMiB < 64 || MaximumDegree < 1 || Ownership is null || Ownership.Length == 0 || Ownership.Any(value => !Enum.IsDefined(value)) || Ownership.Distinct().Count() != Ownership.Length || Ownership.Contains(WorkerReusePolicy.Shared) && Ownership.Length != 1 || Arguments is null || Arguments.Length > 64 || Arguments.Any(value => value is null || value.Contains('\0')))
        {
            throw new InvalidDataException("Invalid installation launch declaration.");
        }
    }

    internal PluginLaunchDeclaration Freeze() => this with
    {
        Arguments = [..Arguments],
        Ownership = [..Ownership]
    };
}
