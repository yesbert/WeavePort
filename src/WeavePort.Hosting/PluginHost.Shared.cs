using System.Text.Json;
using WeavePort.Abstractions;

namespace WeavePort.Hosting;
public sealed partial class PluginHost
{
    private readonly HashSet<SharedPlugin> _shared = [];
    /// <summary>Starts explicitly approved native shared workers. Context configuration must be instance-wide.</summary>
    public async Task<ISharedPlugin> ShareAsync(PluginContext context, ProcessProfile profile, SharedWorkerOptions options, IHostCallbacks callbacks, IEnumerable<string> grants, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(callbacks);
        options.Validate();
        if (profile.ReusePolicy != WorkerReusePolicy.Shared || profile.Protocol != ProcessProtocol.Native)
        {
            throw new NotSupportedException("Shared execution requires an explicitly approved native process profile.");
        }

        var resolved = (ProcessProfile)await ResolveAsync(profile, ExecutionProtections.None, cancellationToken);
        resolved = _scheduler?.PrepareSharedProfile(resolved) ?? resolved;
        var plugin = new SharedPlugin(this, context with { Tenant = "", Configuration = context.Configuration.Clone() }, resolved, options, callbacks, grants.ToHashSet(StringComparer.Ordinal), _pool, _clock);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _shared.Add(plugin);
        }

        try
        {
            await plugin.StartAsync(cancellationToken);
            return plugin;
        }
        catch
        {
            await plugin.DisposeAsync();
            throw;
        }
    }

    internal TenantAdmission RetainTenant(string tenant)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_tenants.TryGetValue(tenant, out var admission))
            {
                admission = new TenantAdmission(tenant, _perTenant, Retain, Release);
                _tenants.Add(tenant, admission);
            }

            admission.References++;
            return admission;
        }
    }

    internal int ActiveCallsFor(string tenant)
    {
        lock (_sync)
        {
            return _tenants.TryGetValue(tenant, out var admission) ? _perTenant - admission.Calls.CurrentCount : 0;
        }
    }

    internal void NotifySharedCapacity() => _scheduler?.NotifyCapacity();
    internal void ReleaseTenant(TenantAdmission admission) => Release(admission);
    internal void RemoveShared(SharedPlugin plugin)
    {
        lock (_sync)
        {
            _shared.Remove(plugin);
        }
    }

    internal Task<InvocationResult> InvokeSharedAsync(SharedInvocationSession session, string operation, JsonElement payload, CancellationToken token)
    {
        if (_scheduler is not null)
        {
            return _scheduler.InvokeSharedAsync(session, operation, payload, token);
        }

        return session.InvokeAsync(operation, payload, token);
    }
}
