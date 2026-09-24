using WeavePort.Internal;
using WeavePort.Abstractions;

namespace WeavePort.Hosting;
/// <summary>Startup failed because the worker advertised a different artifact version. Version metadata is not written to default logs.</summary>
public sealed class PluginVersionMismatchException(string expected, string advertised) : IOException("Worker artifact version mismatch.")
{
    /// <summary>Gets the stable failure code without version metadata.</summary>
    public string ErrorCode => FailureCodes.VersionMismatch;
    /// <summary>Gets the expected and advertised artifact versions.</summary>
    public PluginVersionMismatch Mismatch { get; } = new(expected, advertised);
}
