using Microsoft.Extensions.Logging;
using WeavePort.Internal;
using System.Diagnostics;
using System.Text.Json;
using WeavePort.Abstractions;

namespace WeavePort.Hosting;
internal sealed class PluginSession(SessionBinding binding, TenantAdmission admission, WorkerPool pool, TimeProvider clock, Action<PluginSession, TenantAdmission> removed, ILogger logger) : IPluginSession
{
    private const string Disabled = "disabled";
    private static readonly ActivitySource Traces = new("WeavePort.Hosting");
    private readonly SemaphoreSlim _gate = new(1);
    private Worker? _worker;
    private long _lastUsed = clock.GetTimestamp();
    private readonly object _disposeSync = new();
    private Task? _disposal;
    private volatile bool _disposed;
    private bool _dispatched;
    private JsonElement _termination = JsonSerializer.SerializeToElement(new { });
    private readonly CancellationTokenSource _lifetime = new();
    private string _instance = "";
    public string Instance => _instance;
    public string Tenant => binding.Context.Tenant;

    public async Task<InvocationResult> InvokeAsync(string operation, JsonElement payload, CancellationToken cancellationToken = default)
    {
        long started = Stopwatch.GetTimestamp();
        if (_disposed)
        {
            return Result(Disabled, started, false);
        }

        InvocationScope? parent = InvocationScope.Current.Value;
        if (parent is not null && (parent.Tenant != binding.Context.Tenant || !parent.Active))
        {
            return new InvocationResult("denied", JsonSerializer.SerializeToElement(new { }), "", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        if (!await _gate.WaitAsync(0, CancellationToken.None))
        {
            RuntimeLog.AdmissionRejected(logger, "session-call");
            return Result("busy", started, false);
        }

        try
        {
            return await InvokeAdmittedAsync(operation, payload, parent, started, cancellationToken);
        }
        finally
        {
            try
            {
                _lastUsed = clock.GetTimestamp();
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private async Task<InvocationResult> InvokeAdmittedAsync(string operation, JsonElement payload, InvocationScope? parent, long started, CancellationToken cancellationToken)
    {
        _dispatched = false;
        bool admitted = false;
        string id = "";
        try
        {
            if (_disposed)
            {
                return Result(Disabled, started, false);
            }

            id = Guid.NewGuid().ToString("N");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            deadline.CancelAfter(binding.Profile.Timeout ?? TimeSpan.FromSeconds(5));
            using Activity? activity = Traces.StartActivity("plugin.invoke");
            if (_disposed)
            {
                return Result(Disabled, started);
            }

            admitted = await admission.Calls.WaitAsync(0, CancellationToken.None);
            if (!admitted)
            {
                RuntimeLog.AdmissionRejected(logger, "tenant-calls");
                return Result("busy", started);
            }

            deadline.Token.ThrowIfCancellationRequested();
            await EnsureStartedAsync(deadline.Token);
            string trace = parent?.TraceId ?? activity?.TraceId.ToString() ?? id;
            JsonElement value = await DispatchAsync(new InvokeFrame("invoke", id, operation, payload, binding.Context, trace), parent, deadline.Token);
            return new InvocationResult("ok", value, Instance, Stopwatch.GetElapsedTime(started).TotalMilliseconds, true);
        }
        catch (WorkerCapacityException)
        {
            return Result("busy", started, false);
        }
        catch (OperationCanceledException)
        {
            await StopAsync();
            string status = cancellationToken.IsCancellationRequested ? "cancelled" : "timeout";
            return Result(_disposed ? Disabled : status, started);
        }
        catch (Exception error) when (FailureStatus(error)is not null)
        {
            RuntimeLog.InvocationFailed(logger, Instance, id, _dispatched ? "exchange" : "prepare", error.GetType().Name);
            await StopAsync();
            return Result(FailureStatus(error)!, started);
        }
        finally
        {
            if (admitted)
            {
                admission.Calls.Release();
            }
        }
    }

    private static string? FailureStatus(Exception error) => error switch
    {
        UnauthorizedAccessException => "denied",
        InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException => "protocol-error",
        IOException or System.Net.Http.HttpRequestException or System.Net.Sockets.SocketException => "failed",
        _ => null
    };
    private async Task<JsonElement> DispatchAsync(InvokeFrame frame, InvocationScope? parent, CancellationToken token)
    {
        InvocationScope? ownedScope = null;
        try
        {
            _termination = JsonSerializer.SerializeToElement(new { });
            if (parent is null)
            {
                ownedScope = new InvocationScope(binding.Context.Tenant, frame.TraceId, binding.Grants, token);
            }

            InvocationScope.Current.Value = parent ?? ownedScope;
            _dispatched = true;
            await Frames.WriteAsync(_worker!.Input, frame, WireJson.Default.InvokeFrame, token);
            return await ExchangeAsync(frame.Id, frame.TraceId, token);
        }
        finally
        {
            ownedScope?.Complete();
            InvocationScope.Current.Value = parent;
        }
    }

    private async Task EnsureStartedAsync(CancellationToken token)
    {
        if (_worker is { Running: true })
        {
            return;
        }

        await StopAsync();
        _worker = await pool.AcquireAsync(binding.Profile, binding.Context.Version, binding.Context.Tenant, token);
        _instance = _worker.Instance;
    }

    private async Task<JsonElement> ExchangeAsync(string id, string trace, CancellationToken token)
    {
        var callbackIds = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            JsonElement frame = await _worker!.Reader.ReadAsync(token);
            WorkerEnvelope.Validate(frame);
            if (frame.GetProperty("id").GetString() != id)
            {
                throw new InvalidDataException("Invocation mismatch.");
            }

            string? type = frame.GetProperty("type").GetString();
            if (type == "result")
            {
                return frame.GetProperty("value");
            }

            if (type == "error")
            {
                throw new IOException("Plugin reported an execution error.");
            }

            if (type != "callback")
            {
                throw new InvalidDataException("Unknown frame.");
            }

            string operation = frame.GetProperty("operation").GetString() ?? "";
            string callbackId = frame.GetProperty("callbackId").GetString() ?? "";
            if (!binding.Grants.Contains(operation) || !(InvocationScope.Current.Value?.Allows(operation) ?? false))
            {
                throw new UnauthorizedAccessException();
            }

            if (!(InvocationScope.Current.Value?.TakeCallback() ?? false) || !callbackIds.Add(callbackId))
            {
                throw new InvalidDataException("Callback budget or identity violation.");
            }

            var call = new HostCall(binding.Context, id, operation, frame.GetProperty("payload"), trace);
            JsonElement value = await InvokeCallbackAsync(call, token);
            await Frames.WriteAsync(_worker!.Input, new CallbackResultFrame("callback-result", id, callbackId, value), WireJson.Default.CallbackResultFrame, token);
        }
    }

    private async Task<JsonElement> InvokeCallbackAsync(HostCall call, CancellationToken token)
    {
        if (!await admission.Callbacks.WaitAsync(0, token))
        {
            throw new IOException("Callback capacity exhausted.");
        }

        admission.RetainCallback();
        Task<JsonElement> callback = Task.Run(async () =>
        {
            try
            {
                return await binding.Callbacks.InvokeAsync(call, token);
            }
            catch (Exception error) when (error is not OperationCanceledException and not UnauthorizedAccessException)
            {
                RuntimeLog.CallbackFailed(logger, Instance, call.InvocationId, error.GetType().Name);
                throw new IOException("Host callback failed.", error);
            }
            finally
            {
                admission.ReleaseCallback();
            }
        }, CancellationToken.None);
        JsonElement value;
        try
        {
            value = await callback.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            _ = callback.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
            throw;
        }

        return value;
    }

    private InvocationResult Result(string status, long started, bool? dispatched = null) => new(status, _termination, Instance, Stopwatch.GetElapsedTime(started).TotalMilliseconds, dispatched ?? _dispatched);
    private async Task StopAsync()
    {
        if (_worker is null)
        {
            return;
        }

        Worker worker = _worker;
        _worker = null;
        _termination = await pool.DestroyAsync(worker);
    }

    internal async Task ReleaseIdleAsync(CancellationToken token)
    {
        if (binding.Profile.IdleTimeout is not { } idle || !await _gate.WaitAsync(0, token))
        {
            return;
        }

        try
        {
            if (_worker is not null && clock.GetElapsedTime(_lastUsed) >= idle)
            {
                await StopAsync();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await StopAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            return new ValueTask(_disposal ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        _disposed = true;
        try
        {
            await Cleanup.RunAsync(() => _lifetime.CancelAsync(), ReleaseBindingAsync);
        }
        catch (Exception error)
        {
            RuntimeLog.CleanupFailed(logger, Instance, error.GetType().Name);
            throw;
        }
        finally
        {
            _lifetime.Dispose();
        }
    }

    private async Task ReleaseBindingAsync()
    {
        try
        {
            await RestartAsync();
        }
        finally
        {
            removed(this, admission);
        }
    }
}
