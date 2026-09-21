using System.Diagnostics;
using System.Text.Json;
using WeavePort.Abstractions;

namespace WeavePort.Hosting;
internal sealed class SharedInvocationSession(SharedPlugin plugin, string tenant) : IPluginSession
{
    private SharedWorker? _worker;
    internal SharedPlugin Plugin => plugin;
    internal bool Available => plugin.Available;

    internal bool Reserve()
    {
        _worker = plugin.Reserve();
        return _worker is not null;
    }

    public string Tenant => tenant;
    public string Instance => _worker?.Instance ?? "";

    public async Task<InvocationResult> InvokeAsync(string operation, JsonElement payload, CancellationToken cancellationToken = default)
    {
        long started = Stopwatch.GetTimestamp();
        var empty = JsonSerializer.SerializeToElement(new { });
        if (plugin.Snapshot.Disabled)
        {
            return new("disabled", empty, "", 0);
        }

        if (_worker is null && !Reserve())
        {
            return new("busy", empty, "", 0);
        }

        TenantAdmission? admission = null;
        bool admitted = false;
        bool transferred = false;
        try
        {
            admission = plugin.Host.RetainTenant(tenant);
            admitted = await admission.Calls.WaitAsync(0, CancellationToken.None);
            if (!admitted)
            {
                return new("busy", empty, Instance, 0);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var call = new SharedCall(plugin, admission, tenant, operation, payload, started, cancellationToken);
            transferred = true;
            admission = null; // The call owns admission until terminal acknowledgement, not caller cancellation.
            return await _worker!.InvokeAsync(call);
        }
        catch (OperationCanceledException)
        {
            return new("cancelled", empty, Instance, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        finally
        {
            if (admission is not null)
            {
                if (admitted)
                {
                    admission.Calls.Release();
                }

                plugin.Host.ReleaseTenant(admission);
            }

            if (!transferred)
            {
                _worker!.ReleaseReservation();
            }
        }
    }

    public Task RestartAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

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
        Stop.CancelAfter(plugin.Profile.Timeout ?? TimeSpan.FromSeconds(5));
        Token = Stop.Token;
        Scope = new InvocationScope(tenant, Id, plugin.Grants, Token, plugin.Profile.MaximumCallbacks);
    }

    internal InvocationResult Result(string status, string instance, JsonElement value) => new(status, value, instance, Stopwatch.GetElapsedTime(_started).TotalMilliseconds, Dispatched);
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
