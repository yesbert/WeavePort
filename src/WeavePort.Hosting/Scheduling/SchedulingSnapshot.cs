namespace WeavePort.Hosting;
/// <summary>Scheduler counters; runtime reservations include pristine and uncertain cleanup.</summary>
public sealed record SchedulingSnapshot(int Registrations, int Queued, int Active, int HeavyActive, long Completed, long Rejected, long Evictions, long ColdCalls, string? Failure, WorkerPoolSnapshot Runtime);
