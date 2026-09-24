using System.Text.Json;
using WeavePort.Abstractions;

namespace WeavePort.Hosting;

internal sealed class ScheduledCall(ScheduledPlugin plugin, string operation, JsonElement payload, long enqueued, CancellationToken token)
{
    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new { });
    internal OperationSession? Lease { get; set; }
    internal bool IsLease { get; init; }
    internal TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal ScheduledPlugin Plugin { get; } = plugin;
    internal string Operation { get; } = operation;
    internal JsonElement Payload { get; } = payload;
    internal CancellationToken Token { get; } = token;
    internal long Enqueued { get; } = enqueued;
    internal TaskCompletionSource<ScheduledInvocationResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static ScheduledInvocationResult Rejected(string status, double queued = 0) => new(new InvocationResult(status, Empty, "", queued), queued, 0, false);
}
