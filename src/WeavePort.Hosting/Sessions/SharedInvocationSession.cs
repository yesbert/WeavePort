using WeavePort.Internal;
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
        var empty = JsonSerializer.SerializeToElement(new
        {
        });
        if (plugin.Snapshot.Disabled)
        {
            return new(FailureCodes.Disabled, empty, "", 0);
        }

        if (_worker is null && !Reserve())
        {
            return new(FailureCodes.Busy, empty, "", 0);
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
                return new(FailureCodes.Busy, empty, Instance, 0);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var call = new SharedCall(plugin, admission, tenant, operation, payload, started, cancellationToken);
            transferred = true;
            admission = null; // The call owns admission until terminal acknowledgement, not caller cancellation.
            return await _worker!.InvokeAsync(call);
        }
        catch (OperationCanceledException)
        {
            return new(FailureCodes.Cancelled, empty, Instance, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        finally
        {
            if (admission is not null)
            {
                ReleaseAdmission(admission, admitted);
            }

            if (!transferred)
            {
                _worker!.ReleaseReservation();
            }
        }
    }

    public Task RestartAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    private void ReleaseAdmission(TenantAdmission admission, bool admitted)
    {
        if (admitted)
        {
            admission.Calls.Release();
        }

        plugin.Host.ReleaseTenant(admission);
    }
}
