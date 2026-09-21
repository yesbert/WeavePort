using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Internal;

namespace WeavePort.Hosting;
/// <summary>Optional fair scheduler for reconstructible plugins. Owns one node budget; customer-bound workers never change tenants; reviewed sessions may explicitly opt into cleaned reuse.</summary>
internal sealed partial class PluginScheduler : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly SchedulingOptions _options;
    private readonly int _workerLimit;
    private readonly TimeProvider _clock;
    private readonly PluginHost _host;
    private readonly Dictionary<(string Tenant, string Plugin), ScheduledPlugin> _plugins = [];
    private readonly HashSet<ScheduledPlugin> _active = [];
    private readonly HashSet<ScheduledPlugin> _residents = [];
    private readonly Dictionary<string, int> _tenantRegistrations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _turns = new(StringComparer.Ordinal);
    private readonly List<ScheduledCall> _queue = [];
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _pump;
    private bool _closed;
    private Task? _disposal;
    private string? _failure;
    private long _turn;
    private long _completed;
    private long _rejected;
    private long _evictions;
    private long _coldCalls;
    /// <summary>Creates an independent coordinator. Register only trusted, host-authorized profiles and identities.</summary>
    internal PluginScheduler(PluginHost host, SchedulingOptions options, TimeProvider timeProvider)
    {
        _options = options;
        SchedulingOptions.Validate(_options);
        _clock = timeProvider;
        // Every supported profile reserves at least 64 MiB. This derived ceiling cannot bind before memory does.
        _workerLimit = _options.MaximumWorkers ?? (int)Math.Min(int.MaxValue, _options.MemoryBudgetMiB / 64);
        _host = host;
        _pump = Task.Run(PumpAsync);
    }

    /// <summary>Observes queue, work and underlying worker accounting.</summary>
    public SchedulingSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new(_plugins.Count, _queue.Count, _active.Count, _active.Count(p => p.WorkClass == PluginWorkClass.Heavy), _completed, _rejected, _evictions, _coldCalls, _failure, _host.Snapshot);
            }
        }
    }

    /// <summary>Registers one reconstructible plugin per tenant/plugin key. Dispose before changing version or authority.</summary>
    public async Task<ScheduledPlugin> RegisterAsync(PluginContext context, ExecutionProfile profile, IHostCallbacks callbacks, IEnumerable<string> grants, PluginWorkClass workClass = PluginWorkClass.Normal, ExecutionProtections requiredProtection = ExecutionProtections.None, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context.Plugin);
        ArgumentNullException.ThrowIfNull(callbacks);
        ArgumentNullException.ThrowIfNull(profile);
        if (!Enum.IsDefined(workClass) || profile.MemoryMiB > _options.MemoryBudgetMiB || workClass == PluginWorkClass.Heavy && profile.MemoryMiB > _options.MemoryBudgetMiB / 2)
        {
            throw new ArgumentOutOfRangeException(nameof(profile));
        }

        TimeSpan limit = workClass == PluginWorkClass.Heavy ? _options.HeavyTimeout : _options.NormalTimeout;
        profile = profile with
        {
            Timeout = profile.Timeout is { } requested && requested < limit ? requested : limit,
            IdleTimeout = null
        };
        profile = await profile.ResolveAsync(cancellationToken);
        IPluginSession session = await _host.BindDirectAsync(context, profile, callbacks, grants, requiredProtection, cancellationToken);
        try
        {
            lock (_sync)
            {
                ValidateRegistration(context, workClass);
                var plugin = new ScheduledPlugin(this, session, profile, context, workClass);
                _plugins.Add((context.Tenant, context.Plugin), plugin);
                _tenantRegistrations[context.Tenant] = _tenantRegistrations.GetValueOrDefault(context.Tenant) + 1;
                return plugin;
            }
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private void ValidateRegistration(PluginContext context, PluginWorkClass workClass)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_plugins.ContainsKey((context.Tenant, context.Plugin)) || _plugins.Count >= _options.MaximumRegistrations)
        {
            throw new InvalidOperationException("Registration exists or registry capacity is exhausted.");
        }

        if (workClass == PluginWorkClass.Heavy && (_options.MaximumHeavyCalls == 0 || _plugins.Values.Count(p => p.Tenant == context.Tenant && p.WorkClass == workClass) >= _options.MaximumHeavyPluginsPerTenant))
        {
            throw new InvalidOperationException("Heavy plugin declaration budget exhausted.");
        }
    }

    internal Task<ScheduledInvocationResult> Enqueue(ScheduledPlugin plugin, string operation, JsonElement payload, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        if (operation is SdkOperations.Start or SdkOperations.Next or SdkOperations.Close)
        {
            throw new NotSupportedException("Scheduled SDK streams require operation-wide worker ownership. Use a direct host for streams.");
        }

        lock (_sync)
        {
            string? rejection = AdmissionFailure(plugin, token);
            if (rejection is not null)
            {
                _rejected++;
                return Task.FromResult(ScheduledCall.Rejected(rejection));
            }

            using var buffer = new FrameBuffer();
            JsonSerializer.Serialize(buffer, payload);
            if (buffer.WrittenMemory.Length > _options.MaximumPayloadBytes)
            {
                throw new InvalidDataException("Queued payload exceeds the configured byte limit.");
            }

            var call = new ScheduledCall(plugin, operation, payload.Clone(), _clock.GetTimestamp(), token);
            if (_options.QueueTimeout == TimeSpan.Zero)
            {
                if (!Eligible(plugin, _active.ToArray()) || !Fits(plugin) || !Admit(call))
                {
                    _rejected++;
                    return Task.FromResult(ScheduledCall.Rejected("busy"));
                }

                return call.Completion.Task;
            }

            _queue.Add(call);
            plugin.LastDemand = call.Enqueued;
            Wake();
            return call.Completion.Task;
        }
    }

    internal void NotifyCapacity() => Wake();
    private string? AdmissionFailure(ScheduledPlugin plugin, CancellationToken token)
    {
        if (_closed || plugin.Closed || plugin.Session is SharedInvocationSession shared && shared.Plugin.Snapshot.Disabled)
        {
            return "disabled";
        }

        if (token.IsCancellationRequested)
        {
            return "cancelled";
        }

        if (InvocationScope.Current.Value is not null)
        {
            return "denied";
        }

        return _failure is not null || _queue.Count >= _options.MaximumQueuedCalls || _queue.Count(c => c.Plugin.Tenant == plugin.Tenant) >= _options.MaximumQueuedCallsPerTenant ? "busy" : null;
    }

    private void Wake()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        { /* An existing signal already schedules the next admission scan. */
        }
    }
}
