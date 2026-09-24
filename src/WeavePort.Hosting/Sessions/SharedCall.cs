using System.Diagnostics;
using WeavePort.Internal;
using System.Text.Json;
using WeavePort.Abstractions;

namespace WeavePort.Hosting;

internal sealed class SharedCall
{
    private int _finished;
    private readonly long _started;
    private readonly SharedPlugin _plugin;
    internal string Id { get; } = Guid.NewGuid().ToString("N");
    internal PluginContext Context { get; }
    internal string Operation { get; }
    internal JsonElement Payload { get; }
    internal TenantAdmission Admission { get; }
    internal CancellationTokenSource Stop { get; }
    internal CancellationToken Token { get; }
    internal CancellationToken CallerToken { get; }
    internal InvocationScope Scope { get; }
    internal TaskCompletionSource<InvocationResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal HashSet<string> CallbackIds { get; } = [];
    internal bool CallbackRejected { get; set; }
    internal PluginFailure? Failure { get; set; }
    internal bool Abandoned { get; set; }
    internal bool Dispatched { get; set; }
    internal bool Finished => Volatile.Read(ref _finished) != 0;

    internal SharedCall(SharedPlugin plugin, TenantAdmission admission, string tenant, string operation, JsonElement payload, long started, CancellationToken token)
    {
        _plugin = plugin;
        _started = started;
        Admission = admission;
        Context = plugin.Context with
        {
            Tenant = tenant
        };
        Operation = operation;
        Payload = payload.Clone();
        CallerToken = token;
        Stop = CancellationTokenSource.CreateLinkedTokenSource(token, plugin.Lifetime);
        Stop.CancelAfter(plugin.Profile.Timeout ?? InvocationPolicy.DefaultTimeout);
        Token = Stop.Token;
        Scope = new InvocationScope(tenant, Id, plugin.Grants, Token, plugin.Profile.MaximumCallbacks);
    }

    internal InvocationResult Result(string status, string instance, JsonElement value) => new(status, value, instance, Stopwatch.GetElapsedTime(_started).TotalMilliseconds, Dispatched)
    {
        Failure = DescribeFailure(status)
    };
    private PluginFailure? DescribeFailure(string status)
    {
        if (Failure is not null || status == FailureCodes.Ok)
        {
            return Failure;
        }

        string phase = Dispatched ? FailurePhases.Exchange : FailurePhases.Prepare;
        return new PluginFailure(status, phase, Id);
    }

    internal void Finish(string status, string instance, JsonElement value)
    {
        if (Interlocked.Exchange(ref _finished, 1) != 0)
        {
            return;
        }

        Scope.Complete();
        Completion.TrySetResult(Result(status, instance, value));
        Admission.Calls.Release();
        _plugin.Host.ReleaseTenant(Admission);
        _plugin.Host.NotifySharedCapacity();
        _ = CancelAndDisposeAsync();
    }

    private async Task CancelAndDisposeAsync()
    {
        try
        {
            await Stop.CancelAsync();
        }
        catch (AggregateException)
        { /* Consumer cancellation callbacks cannot prevent admission cleanup. */
        }
        finally
        {
            Stop.Dispose();
        }
    }
}
