using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;

internal static partial class SchedulingChecks
{
    private sealed class Nested(ScheduledPlugin target) : IHostCallbacks
    {
        internal string? Status;
        public async ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken token)
        {
            Status = (await target.InvokeAsync("echo", Empty, token)).Status;
            return Empty;
        }
    }
    private sealed class ThrowOnCancel : IHostCallbacks
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken token)
        {
            using var registration = token.Register(() => throw new InvalidOperationException("fixture-cancellation-failure"));
            Entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return Empty;
        }
    }

    private sealed class Holds : IHostCallbacks
    {
        internal System.Collections.Concurrent.ConcurrentQueue<TaskCompletionSource> Entered { get; } = new();
        internal volatile bool AutoRelease;
        public async ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken token)
        {
            if (AutoRelease)
            {
                return Empty;
            }

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Entered.Enqueue(gate);
            await gate.Task.WaitAsync(token);
            return Empty;
        }
        internal void ReleaseOne()
        {
            if (Entered.TryDequeue(out var gate))
            {
                gate.TrySetResult();
            }
        }
        internal void ReleaseAll()
        {
            while (Entered.TryDequeue(out var gate))
            {
                gate.TrySetResult();
            }
        }
    }
}
