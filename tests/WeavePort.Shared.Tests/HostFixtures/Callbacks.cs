using System.Diagnostics;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;

internal sealed class Callbacks(int barrierSize = 1) : IHostCallbacks
{
    private readonly TaskCompletionSource _barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _active;
    private int _entered;
    public int MaximumActive { get; private set; }
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken token)
    {
        if (call.Operation == "throw")
        {
            throw new InvalidOperationException("Deliberate callback failure.");
        }

        if (call.Operation == "entered")
        {
            Entered.TrySetResult();
        }

        if (call.Operation == "barrier")
        {
            await ReachBarrierAsync(token);
        }
        return JsonSerializer.SerializeToElement(new
        {
            tenant = call.Context.Tenant,
            value = call.Payload
        });
    }
    private async Task ReachBarrierAsync(CancellationToken token)
    {
        int active = Interlocked.Increment(ref _active);
        lock (_barrier)
        {
            MaximumActive = Math.Max(MaximumActive, active);
        }

        if (Interlocked.Increment(ref _entered) == barrierSize)
        {
            _barrier.TrySetResult();
        }

        await _barrier.Task.WaitAsync(token);
        Interlocked.Decrement(ref _active);
    }

}
