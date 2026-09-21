using System.Text.Json;
using WeavePort.Abstractions;

namespace WeavePort.Hosting;
internal sealed partial class PluginScheduler
{
    internal ProcessProfile PrepareSharedProfile(ProcessProfile profile)
    {
        if (profile.WorkClass == PluginWorkClass.Heavy && (_options.MaximumHeavyCalls == 0 || profile.MemoryMiB > _options.MemoryBudgetMiB / 2))
        {
            throw new ArgumentOutOfRangeException(nameof(profile), "Heavy shared deployment exceeds its approved scheduling budget.");
        }

        TimeSpan limit = profile.WorkClass == PluginWorkClass.Heavy ? _options.HeavyTimeout : _options.NormalTimeout;
        return profile with
        {
            Timeout = profile.Timeout is { } requested && requested < limit ? requested : limit
        };
    }

    internal async Task<InvocationResult> InvokeSharedAsync(SharedInvocationSession session, string operation, JsonElement payload, CancellationToken token)
    {
        var context = session.Plugin.Context with
        {
            Tenant = session.Tenant
        };
        var registration = new ScheduledPlugin(this, session, session.Plugin.Profile, context, session.Plugin.Profile.WorkClass);
        return (await Enqueue(registration, operation, payload, token)).Result;
    }

    internal async ValueTask<IPluginSession> AcquireOperationAsync(ScheduledPlugin plugin, CancellationToken token)
    {
        ScheduledCall call;
        lock (_sync)
        {
            string? rejected = AdmissionFailure(plugin, token);
            if (rejected is not null)
            {
                throw new IOException("Stream admission: " + rejected);
            }

            call = new ScheduledCall(plugin, "", JsonSerializer.SerializeToElement(new { }), _clock.GetTimestamp(), token)
            {
                IsLease = true
            };
            if (_options.QueueTimeout == TimeSpan.Zero)
            {
                if (!Eligible(plugin, _active.ToArray()) || !Fits(plugin) || !Admit(call))
                {
                    throw new IOException("Stream admission: busy");
                }
            }
            else
            {
                _queue.Add(call);
            }

            plugin.LastDemand = call.Enqueued;
            Wake();
        }

        var result = await call.Completion.Task;
        if (result.Result.Status != "ok")
        {
            throw new IOException("Stream admission: " + result.Result.Status);
        }

        return call.Lease!;
    }

    private static async Task<InvocationResult> RunLeaseAsync(ScheduledCall call, CancellationToken token)
    {
        var result = new InvocationResult("ok", JsonSerializer.SerializeToElement(new { }), call.Plugin.Instance, 0);
        var direct = await ((IPluginOperationSession)call.Plugin.Session).AcquireOperationAsync(token);
        call.Lease = new OperationSession(direct, call.Released, token);
        call.Completion.TrySetResult(new(result, 0, 0, false));
        // The lease owns cancellation and releases only after its active exchange has stopped.
        try
        {
            await call.Released.Task.WaitAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            await call.Lease.DisposeAsync();
        }

        return result;
    }
}

internal sealed class OperationSession(IPluginSession session, TaskCompletionSource released, CancellationToken token) : IPluginSession
{
    private readonly SemaphoreSlim _gate = new(1);
    private readonly CancellationTokenSource _stop = CancellationTokenSource.CreateLinkedTokenSource(token);
    private bool _disposed;
    public string Tenant => session.Tenant;
    public string Instance => session.Instance;

    public async Task<InvocationResult> InvokeAsync(string operation, JsonElement payload, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        await _gate.WaitAsync(linked.Token);
        try
        {
            return await session.InvokeAsync(operation, payload, linked.Token);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task RestartAsync(CancellationToken cancellationToken = default) => _disposed ? Task.CompletedTask : session.RestartAsync(cancellationToken);
    private readonly object _disposeSync = new();
    private Task? _disposal;
    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            return new(_disposal ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        _disposed = true;
        try
        {
            await WeavePort.Internal.Cleanup.RunAsync(() => _stop.CancelAsync(), async () =>
            {
                await _gate.WaitAsync();
                try
                {
                    await session.DisposeAsync();
                }
                finally
                {
                    _gate.Release();
                }
            });
        }
        finally
        {
            _stop.Dispose();
            released.TrySetResult();
        }
    }
}
