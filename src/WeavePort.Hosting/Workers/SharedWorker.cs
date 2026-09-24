using WeavePort.Internal;
using System.Text.Json;

namespace WeavePort.Hosting;
internal sealed partial class SharedWorker(SharedPlugin plugin, WorkerPool pool, TimeProvider clock) : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _writer = new(1);
    private readonly Dictionary<string, SharedCall> _calls = [];
    private readonly CancellationTokenSource _lifetime = new();
    private Worker? _worker;
    private Task? _reader;
    private Task? _watch;
    private Task? _disposal;
    private int _active;
    private bool _ready;
    private long _lastFrame;
    internal string Instance => _worker?.Instance ?? "";

    internal bool Ready
    {
        get
        {
            lock (_sync)
            {
                return _ready;
            }
        }
    }

    internal int Active
    {
        get
        {
            lock (_sync)
            {
                return _active;
            }
        }
    }

    internal int Abandoned
    {
        get
        {
            lock (_sync)
            {
                return _calls.Values.Count(c => c.Abandoned);
            }
        }
    }

    internal bool Available
    {
        get
        {
            lock (_sync)
            {
                return _ready && _active < plugin.Options.Degree;
            }
        }
    }

    internal async Task StartAsync(CancellationToken token)
    {
        _worker = await pool.StartSharedAsync(plugin.Profile, plugin.Context.Version, token);
        await SendAsync(new { type = FrameKinds.Configure, degree = plugin.Options.Degree }, token);
        lock (_sync)
        {
            _channelStop = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _ready = true;
            _lastFrame = clock.GetTimestamp();
        }

        _reader = Task.Run(ReadLoopAsync, CancellationToken.None);
        _watch = Task.Run(WatchAsync, CancellationToken.None);
    }

    internal bool TryReserve()
    {
        lock (_sync)
        {
            if (!_ready || _active >= plugin.Options.Degree)
            {
                return false;
            }

            _active++;
            return true;
        }
    }

    internal void ReleaseReservation()
    {
        lock (_sync)
        {
            _active--;
        }
    }

    private async Task<WeavePort.Abstractions.InvocationResult> InvokeCoreAsync(SharedCall call)
    {
        if (!RegisterCall(call, out Worker worker))
        {
            // Finishing releases host admission; never acquire that lock while holding the worker lock.
            call.Finish(FailureCodes.Failed, Instance, Empty);
            return await call.Completion.Task;
        }

        bool dispatchCompleted = false;
        try
        {
            call.Dispatched = true;
            await SendAsync(new InvokeFrame("invoke", call.Id, call.Operation, call.Payload, call.Context, call.Id), call.Token, worker);
            dispatchCompleted = true;
            return await call.Completion.Task.WaitAsync(call.Token);
        }
        catch (OperationCanceledException) when (call.Token.IsCancellationRequested)
        {
            if (call.Completion.Task.IsCompleted)
            {
                return await call.Completion.Task;
            }

            call.Scope.Complete();
            lock (_sync)
            {
                if (!call.Finished)
                {
                    call.Abandoned = true;
                }
            }

            if (dispatchCompleted)
            {
                _ = CancelAsync(call, worker);
            }
            else
            {
                // A cancelled write may have emitted only part of a frame. Never reuse that channel.
                Retire(worker);
            }

            return call.Result(call.CallerToken.IsCancellationRequested ? FailureCodes.Cancelled : plugin.Lifetime.IsCancellationRequested ? FailureCodes.Disabled : FailureCodes.Timeout, Instance, Empty);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ObjectDisposedException or OperationCanceledException)
        {
            Retire(worker);
            return await call.Completion.Task;
        }
    }

    private bool RegisterCall(SharedCall call, out Worker worker)
    {
        lock (_sync)
        {
            worker = _worker!;
            if (!_ready)
            {
                _active--;
                return false;
            }

            if (_calls.Count == 0)
            {
                _lastFrame = clock.GetTimestamp();
            }

            _calls.Add(call.Id, call);
            return true;
        }
    }

    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new { });
    private async Task SendAsync<T>(T frame, CancellationToken token, Worker? expectedWorker = null)
    {
        await _writer.WaitAsync(token);
        try
        {
            Worker worker = expectedWorker ?? _worker!;
            if (!ReferenceEquals(worker, _worker))
            {
                throw new IOException("Worker channel was replaced.");
            }

            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(frame, JsonOptions);
            if (bytes.Length > Frames.MaximumBytes)
            {
                throw new InvalidDataException("Frame exceeds limit.");
            }

            await worker.Input.WriteAsync(bytes, token);
            await worker.Input.WriteAsync("\n"u8.ToArray(), token);
            await worker.Input.FlushAsync(token);
        }
        finally
        {
            _writer.Release();
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private async Task CancelAsync(SharedCall call, Worker worker)
    {
        try
        {
            if (call.Finished)
            {
                return;
            }

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            deadline.CancelAfter(plugin.Options.CancellationGrace);
            await SendAsync(new { type = FrameKinds.Cancel, id = call.Id }, deadline.Token, worker);
            if (Abandoned >= plugin.Options.MaximumAbandonedCalls)
            {
                Retire(worker);
                return;
            }

            await call.Completion.Task.WaitAsync(deadline.Token);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or OperationCanceledException or TimeoutException or ObjectDisposedException)
        {
            Retire(worker);
        }
    }

    private void Retire(Worker? expectedWorker = null)
    {
        lock (_sync)
        {
            if (expectedWorker is not null && !ReferenceEquals(expectedWorker, _worker))
            {
                return;
            }

            _ready = false;
            if (_channelStop is not null)
            {
                // Cancellation callbacks belong to consumer code and may throw.
                _ = _channelStop.CancelAsync().ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
            }
        }
    // The reader owns all retirement and replacement work.
    }

    private CancellationTokenSource? _channelStop;
    private async Task WatchAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100), clock);
        try
        {
            while (await timer.WaitForNextTickAsync(_lifetime.Token))
            {
                bool silent;
                lock (_sync)
                {
                    silent = _calls.Count > 0 && clock.GetElapsedTime(_lastFrame) >= plugin.Options.SilenceTimeout;
                }

                if (!silent)
                {
                    continue;
                }

                Retire();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        // Owned shutdown ends the watchdog; the reader performs channel retirement.
        }
    }

    internal async Task DrainAsync(TimeSpan timeout)
    {
        Task[] pending;
        lock (_sync)
        {
            _ready = false;
            pending = _calls.Values.Select(c => c.Completion.Task).ToArray();
        }

        try
        {
            await Task.WhenAll(pending).WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
        // The drain grace expired. The owner proceeds to disposal, which retires
        // the worker and completes any remaining calls through the reader.
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            return new(_disposal ??= Task.Run(DisposeCoreAsync, CancellationToken.None));
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            await WeavePort.Internal.Cleanup.RunAsync(() => _lifetime.CancelAsync(), ReleaseAsync);
        }
        finally
        {
            _channelStop?.Dispose();
            _lifetime.Dispose();
        }
    }

    private async Task ReleaseAsync()
    {
        Retire();
        try
        {
            if (_reader is not null)
            {
                await _reader;
            }

            if (_watch is not null)
            {
                await _watch;
            }
        }
        finally
        {
            if (_worker is not null)
            {
                await pool.DestroyAsync(_worker);
            }
        }
    }
}
