using WeavePort.Internal;
using System.Text.Json;
using WeavePort.Abstractions;

namespace WeavePort.Hosting;

internal sealed partial class SharedWorker
{
    private async Task ReadLoopAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            try
            {
                await ReadFramesAsync();
            }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException or OperationCanceledException or ObjectDisposedException)
            {
                if (!await RecoverChannelAsync(error))
                {
                    return;
                }
            }
        }
    }

    private async Task ReadFramesAsync()
    {
        while (true)
        {
            JsonElement frame = await _worker!.Reader.ReadAsync(_channelStop!.Token);
            WorkerEnvelope.ValidateExchange(frame);
            string id = frame.GetProperty(WireFields.Id).GetString() ?? "";
            SharedCall call;
            lock (_sync)
            {
                _lastFrame = clock.GetTimestamp();
                if (!_calls.TryGetValue(id, out call!))
                {
                    throw new InvalidDataException("Unknown invocation identity.");
                }
            }

            DispatchFrame(call, frame);
        }
    }

    private async Task<bool> RecoverChannelAsync(Exception primary)
    {
        Retire(_worker);
        try
        {
            await pool.DestroyAsync(_worker!);
        }
        catch (Exception cleanup)
        {
            var combined = new AggregateException("Shared channel and cleanup failed.", primary, cleanup);
            FailPending(combined, true);
            plugin.Disable(cleanup.GetType().Name);
            throw combined;
        }

        FailPending(primary, false);
        if (_lifetime.IsCancellationRequested || !plugin.AllowRestart())
        {
            return false;
        }

        return await ReplaceAsync();
    }

    private void DispatchFrame(SharedCall call, JsonElement frame)
    {
        string type = frame.GetProperty(WireFields.Type).GetString() ?? "";
        if (type == FrameKinds.Callback)
        {
            QueueCallback(call, frame);
            return;
        }

        if (type is not (FrameKinds.Result or FrameKinds.Error or FrameKinds.Cancelled))
        {
            throw new InvalidDataException("Unsupported shared frame.");
        }

        bool cleanupFailed = type == FrameKinds.Error && frame.TryGetProperty(WireFields.Code, out var code) && code.GetString() == FailureCodes.CleanupError;
        if (type == FrameKinds.Error)
        {
            string reported = ReadReportedFailureCode(frame);
            call.Failure = new PluginFailure(FailureCode.Normalize(reported), FailurePhases.Exchange, call.Id, cleanupFailed);
            pool.ReportFailure(Instance, call.Failure, new WorkerExecutionException(reported, cleanupFailed));
        }

        JsonElement value = type == FrameKinds.Result ? frame.GetProperty(WireFields.Value) : Empty;
        lock (_sync)
        {
            _calls.Remove(call.Id);
            _active--;
        }

        string status = type switch
        {
            FrameKinds.Result => FailureCodes.Ok,
            FrameKinds.Cancelled => FailureCodes.Cancelled,
            _ => FailureCodes.Failed
        };
        call.Finish(status, Instance, value);
        if (cleanupFailed)
        {
            throw new InvalidDataException("Shared invocation cleanup failed.");
        }
    }

    private static string ReadReportedFailureCode(JsonElement frame)
    {
        if (frame.TryGetProperty(WireFields.PrimaryCode, out JsonElement primary))
        {
            return primary.GetString()!;
        }

        if (frame.TryGetProperty(WireFields.Code, out JsonElement code))
        {
            return code.GetString()!;
        }

        return FailureCodes.Failed;
    }

    private void FailPending(Exception error, bool cleanupFailed)
    {
        SharedCall[] calls;
        lock (_sync)
        {
            _ready = false;
            calls = _calls.Values.ToArray();
            _calls.Clear();
            _active -= calls.Length;
        }

        foreach (var call in calls)
        {
            string code = error is InvalidDataException or JsonException ? FailureCodes.ProtocolError : FailureCodes.Failed;
            call.Failure = new PluginFailure(code, FailurePhases.Exchange, call.Id, cleanupFailed);
            pool.ReportFailure(Instance, call.Failure, error);
            call.Finish(FailureCodes.Failed, Instance, Empty);
        }
    }

    private async Task<bool> ReplaceAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            Worker? replacement = null;
            try
            {
                replacement = await pool.StartSharedAsync(plugin.Profile, plugin.Context.Version, _lifetime.Token);
                _worker = replacement;
                await SendAsync(new
                {
                    type = FrameKinds.Configure,
                    degree = plugin.Options.Degree
                }, _lifetime.Token, replacement);
                lock (_sync)
                {
                    _channelStop?.Dispose();
                    _channelStop = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                    _ready = true;
                    _lastFrame = clock.GetTimestamp();
                }

                plugin.Host.NotifySharedCapacity();
                return true;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or OperationCanceledException or InvalidOperationException)
            {
                await DestroyReplacementAsync(replacement);
                if (_lifetime.IsCancellationRequested || !plugin.AllowRestart())
                {
                    return false;
                }
            }
        }

        return false;
    }

    private async Task DestroyReplacementAsync(Worker? replacement)
    {
        if (replacement is not null)
        {
            await pool.DestroyAsync(replacement);
        }
    }
}
