using WeavePort.Internal;
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

            call = new ScheduledCall(plugin, "", JsonSerializer.SerializeToElement(new
            {
            }), _clock.GetTimestamp(), token)
            {
                IsLease = true
            };
            if (_options.QueueTimeout == TimeSpan.Zero && (!Eligible(plugin, _active.ToArray()) || !Fits(plugin) || !Admit(call)))
            {
                throw new IOException("Stream admission: busy");
            }

            if (_options.QueueTimeout != TimeSpan.Zero)
            {
                _queue.Add(call);
            }

            plugin.LastDemand = call.Enqueued;
            Wake();
        }

        var result = await call.Completion.Task;
        if (result.Result.Status != FailureCodes.Ok)
        {
            throw new IOException("Stream admission: " + result.Result.Status);
        }

        return call.Lease!;
    }

    private static async Task<InvocationResult> RunLeaseAsync(ScheduledCall call, CancellationToken token)
    {
        var result = new InvocationResult(FailureCodes.Ok, JsonSerializer.SerializeToElement(new
        {
        }), call.Plugin.Instance, 0);
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
