namespace WeavePort.Hosting;
internal sealed class InvocationScope(string tenant, string traceId, HashSet<string> grants, CancellationToken cancellation)
{
    internal static readonly AsyncLocal<InvocationScope?> Current = new();
    private int _callbacks;
    private volatile bool _completed;
    internal bool Active => !_completed && !cancellation.IsCancellationRequested;

    internal void Complete() => _completed = true;
    internal string Tenant { get; } = tenant;

    internal bool Allows(string operation) => Active && grants.Contains(operation);
    internal string TraceId { get; } = traceId;

    internal bool TakeCallback() => Active && Interlocked.Increment(ref _callbacks) <= 8;
}
