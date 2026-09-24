namespace WeavePort.Hosting;
/// <summary>Requested operating-system restrictions, not proof against kernel/runtime exploits.</summary>
[Flags]
public enum ExecutionProtections
{
    /// <summary>No sandbox restriction is required.</summary>
    None = 0,
    /// <summary>Restrict the worker filesystem view.</summary>
    RestrictedFileSystem = 1,
    /// <summary>Disable worker network access.</summary>
    DisabledNetwork = 2,
    /// <summary>Enforce worker CPU/memory/process limits through the execution environment.</summary>
    HardResourceLimits = 4
}
