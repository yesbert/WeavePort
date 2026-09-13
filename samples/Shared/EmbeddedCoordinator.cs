using WeavePort.Hosting;

namespace WeavePort.Samples;
internal sealed class CoordinatorRejectedException(string status) : InvalidOperationException("Coordinator refused operation: " + status)
{
    internal string Status { get; } = status;
}

internal sealed record CoordinatorSnapshot(bool Accepting, int Active, long Completed, long Failed, bool CancellationRequested, bool ShutdownFinished, string[] CleanupErrors, WorkerPoolSnapshot Runtime)
{
    internal bool Clean => ShutdownFinished && Active == 0 && CleanupErrors.Length == 0 && Runtime.Workers == 0 && Runtime.Bindings == 0 && Runtime.Tenants == 0 && Runtime.MaintenanceFailure is null;
}

// Copyable composition source, not a published package contract or a security boundary.
internal sealed class EmbeddedCoordinator : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly PluginHost _host;
    private readonly int _maximumOperations;
    private readonly TimeProvider _clock;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<string> _cleanupErrors = [];
    private readonly Func<PluginHost, Task> _disposeHost;
    private bool _stopping;
    private int _active;
    private long _completed;
    private long _failed;
    internal EmbeddedCoordinator(int maximumOperations = 8, WorkerPoolOptions? options = null, TimeProvider? clock = null, Func<PluginHost, Task>? disposeHost = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumOperations, 1);
        _maximumOperations = maximumOperations;
        _clock = clock ?? TimeProvider.System;
        _host = new PluginHost(options: options ?? new WorkerPoolOptions(MaximumPristineWorkers: 0), timeProvider: _clock);
        // Optional internal seam verifies failed cleanup reporting; normal composition always disposes the owned host.
        _disposeHost = disposeHost ?? (host => host.DisposeAsync().AsTask());
    }

    internal Task Completion => _completion.Task;

    internal CoordinatorSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new(!_stopping, _active, _completed, _failed, _stop.IsCancellationRequested, _completion.Task.IsCompleted, _cleanupErrors.ToArray(), _host.Snapshot);
            }
        }
    }

    // The delegate owns and awaits its sessions; it must never dispose or retain the shared host.
    internal async Task<T> RunAsync<T>(Func<PluginHost, CancellationToken, Task<T>> operation, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        token.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_stopping)
            {
                throw new CoordinatorRejectedException("stopping");
            }

            if (_active == _maximumOperations)
            {
                throw new CoordinatorRejectedException("busy");
            }

            _active++;
        }

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
            linked.Token.ThrowIfCancellationRequested();
            T result = await operation(_host, linked.Token);
            lock (_sync)
            {
                _completed++;
            }

            return result;
        }
        catch
        {
            lock (_sync)
            {
                _failed++;
            }

            throw;
        }
        finally
        {
            lock (_sync)
            {
                _active--;
                if (_stopping && _active == 0)
                {
                    _drained.TrySetResult();
                }
            }
        }
    }

    // First call fixes the grace policy. Later calls only observe the same lifecycle.
    internal async Task<CoordinatorSnapshot> StopAsync(TimeSpan grace, TimeSpan observation)
    {
        ValidateDuration(grace);
        ValidateDuration(observation);
        bool start;
        lock (_sync)
        {
            start = !_stopping;
            _stopping = true;
            if (_active == 0)
            {
                _drained.TrySetResult();
            }
        }

        if (start)
        {
            _ = ShutdownAsync(grace);
        }

        try
        {
            await Completion.WaitAsync(grace + observation, _clock);
        }
        catch (TimeoutException)
        { /* Observation expired; lifetime and accounting remain live. */
        }

        return Snapshot;
    }

    private async Task ShutdownAsync(TimeSpan grace)
    {
        try
        {
            Task cancellation = Task.CompletedTask;
            try
            {
                await _drained.Task.WaitAsync(grace, _clock);
            }
            catch (TimeoutException)
            {
                cancellation = ObserveCleanupAsync(() => _stop.CancelAsync());
            }

            // Start disposal even if a cancellation callback ignores cancellation or throws.
            await Task.WhenAll(cancellation, ObserveCleanupAsync(() => _disposeHost(_host)));
            await _drained.Task;
        }
        catch (Exception error)
        {
            RecordCleanupError(error);
        }
        finally
        {
            _stop.Dispose();
            _completion.TrySetResult();
        }
    }

    private async Task ObserveCleanupAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception error)
        {
            RecordCleanupError(error);
        }
    }

    private void RecordCleanupError(Exception error)
    {
        lock (_sync)
        {
            _cleanupErrors.Add(error.GetType().Name);
        }
    }

    private static void ValidateDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero || duration > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }
    }

    public async ValueTask DisposeAsync()
    {
        var result = await StopAsync(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        if (!result.Clean)
        {
            throw new InvalidOperationException("Coordinator shutdown incomplete; inspect Snapshot and Completion before releasing application dependencies.");
        }
    }
}
