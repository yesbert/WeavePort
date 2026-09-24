namespace WeavePort.Hosting;
/// <summary>Complete invocation outcome. Execution includes startup; queue expiry never dispatches.</summary>
public sealed record ScheduledInvocationResult(WeavePort.Abstractions.InvocationResult Result, double QueueMs, double ExecutionMs, bool Cold);
