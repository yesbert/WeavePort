using WeavePort.Abstractions;

namespace WeavePort.Hosting;
/// <summary>Explicitly opted-in local exception details. The destination owns access control and retention.</summary>
/// <param name = "WorkerInstance">Worker identity, if startup completed.</param>
/// <param name = "Failure">Safe correlated failure information.</param>
/// <param name = "Exception">Local exception, including all retained cleanup causes. May contain sensitive data.</param>
public sealed record FailureDiagnostic(string WorkerInstance, PluginFailure Failure, Exception Exception);
