using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WeavePort.Internal;
using System.Text.Json;
using WeavePort.Abstractions;

namespace WeavePort.Hosting;
/// <summary>Owns immutable bindings and one shared worker budget. Deploy one coordinator per intended budget boundary.</summary>
public sealed class PluginHost : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<string, TenantAdmission> _tenants = new(StringComparer.Ordinal);
    private readonly HashSet<PluginSession> _sessions = [];
    private readonly int _perTenant;
    private readonly WorkerPool _pool;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _maintenance;
    private string? _maintenanceFailure;
    private bool _disposed;
    private Task? _disposal;
    /// <summary>Creates a coordinator with explicit shared limits and an injectable clock.</summary>
    public PluginHost(int maximumCallsPerTenant = 4, WorkerPoolOptions? options = null, TimeProvider? timeProvider = null) : this(NullLogger<PluginHost>.Instance, maximumCallsPerTenant, options, timeProvider)
    {
    }

    /// <summary>Creates a coordinator using caller-owned logging; events omit payloads and exception messages.</summary>
    public PluginHost(ILogger<PluginHost> logger, int maximumCallsPerTenant = 4, WorkerPoolOptions? options = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCallsPerTenant, 1);
        options ??= new WorkerPoolOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaximumWorkers, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MemoryBudgetMiB, 64);
        ArgumentOutOfRangeException.ThrowIfNegative(options.MaximumPristineWorkers);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaximumWorkersPerTenant, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MemoryBudgetPerTenantMiB, 64);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaximumConcurrentStarts, 1);
        if (options.MaximumPristineWorkers > options.MaximumWorkers || options.PristineLifetime <= TimeSpan.Zero || options.MaintenanceInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        _perTenant = maximumCallsPerTenant;
        _clock = timeProvider ?? TimeProvider.System;
        _pool = new WorkerPool(options, _clock, _logger);
        _maintenance = MaintainLoopAsync(options.MaintenanceInterval ?? TimeSpan.FromSeconds(1));
    }

    /// <summary>Returns coordinator accounting; memory values represent adapter reservations rather than measured RSS or universally enforced limits.</summary>
    public WorkerPoolSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return _pool.Snapshot(_sessions.Count, _tenants.Count, _maintenanceFailure);
            }
        }
    }

    /// <summary>Sets a shared pristine target for a resolved execution profile/version. Zero removes the target. Contains no customer context.</summary>
    public async Task PrewarmAsync(ExecutionProfile profile, string version, int count, CancellationToken cancellationToken = default, ExecutionProtection requiredProtection = ExecutionProtection.None)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        ExecutionProfile resolved = await ResolveAsync(profile, requiredProtection, cancellationToken);
        await _pool.PrewarmAsync(resolved, version, count, cancellationToken);
    }

    /// <summary>Resolves an execution profile and creates an independent binding with explicit callback grants. Idle release is opt-in.</summary>
    public async Task<IPluginSession> BindAsync(PluginContext context, ExecutionProfile profile, IHostCallbacks callbacks, IEnumerable<string> grants, CancellationToken cancellationToken = default, ExecutionProtection requiredProtection = ExecutionProtection.None)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context.Tenant);
        ExecutionProfile resolved = await ResolveAsync(profile, requiredProtection, cancellationToken);
        PluginContext immutable = context with
        {
            Configuration = context.Configuration.Clone()
        };
        HashSet<string> authority = grants.ToHashSet(StringComparer.Ordinal);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_tenants.TryGetValue(context.Tenant, out TenantAdmission? admission))
            {
                admission = new TenantAdmission(context.Tenant, _perTenant, Retain, Release);
                _tenants.Add(context.Tenant, admission);
            }

            admission.References++;
            var session = new PluginSession(new SessionBinding(immutable, resolved, callbacks, authority), admission, _pool, _clock, Remove, _logger);
            _sessions.Add(session);
            return session;
        }
    }

    private static async Task<ExecutionProfile> ResolveAsync(ExecutionProfile profile, ExecutionProtection required, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if ((profile.Protection & required) != required)
        {
            throw new NotSupportedException("Selected execution profile cannot satisfy required protection.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(profile.MemoryMiB, 64);
        if (profile.Timeout <= TimeSpan.Zero || profile.Timeout > TimeSpan.FromMilliseconds(uint.MaxValue - 1) || profile.IdleTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(profile));
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        return await profile.ResolveAsync(deadline.Token);
    }

    private void Retain(TenantAdmission admission)
    {
        lock (_sync)
        {
            admission.References++;
        }
    }

    private void Release(TenantAdmission admission)
    {
        lock (_sync)
        {
            if (--admission.References != 0)
            {
                return;
            }

            _tenants.Remove(admission.Tenant);
        }
    }

    private void Remove(PluginSession session, TenantAdmission admission)
    {
        lock (_sync)
        {
            if (_sessions.Remove(session))
            {
                Release(admission);
            }
        }
    }

    /// <summary>Runs idle release, quarantine cleanup and pristine replenishment. Also runs periodically.</summary>
    public async Task MaintainAsync(CancellationToken cancellationToken = default)
    {
        PluginSession[] sessions;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            sessions = _sessions.ToArray();
        }

        foreach (PluginSession session in sessions)
        {
            await session.ReleaseIdleAsync(cancellationToken);
        }

        await _pool.MaintainAsync(cancellationToken);
    }

    private async Task MaintainLoopAsync(TimeSpan interval)
    {
        using var timer = new PeriodicTimer(interval, _clock);
        try
        {
            while (await timer.WaitForNextTickAsync(_lifetime.Token))
            {
                try
                {
                    await MaintainAsync(_lifetime.Token);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    lock (_sync)
                    {
                        _maintenanceFailure = error.GetType().Name;
                    }

                    RuntimeLog.MaintenanceFailed(_logger, error.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        // Cancellation is the normal end of the owned maintenance loop.
        }
    }

    /// <summary>Disables bindings, stops maintenance and attempts all worker removals. Unconfirmed cleanup is reported.</summary>
    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            return new ValueTask(_disposal ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        PluginSession[] sessions;
        lock (_sync)
        {
            _disposed = true;
            sessions = _sessions.ToArray();
        }

        try
        {
            await Cleanup.RunAsync(() => _lifetime.CancelAsync(), () => ReleaseResourcesAsync(sessions));
        }
        finally
        {
            _lifetime.Dispose();
        }
    }

    private async Task ReleaseResourcesAsync(PluginSession[] sessions)
    {
        List<Exception> errors = [];
        try
        {
            await _maintenance;
        }
        catch (Exception error)
        {
            errors.Add(error);
        }

        foreach (PluginSession session in sessions)
        {
            try
            {
                await session.DisposeAsync();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }

        try
        {
            await _pool.DisposeAsync();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }

        if (errors.Count != 0)
        {
            throw new AggregateException("Host cleanup was incomplete.", errors);
        }
    }
}
