using Microsoft.Extensions.Logging;
using WeavePort.Internal;
using System.Diagnostics;
using System.Text.Json;
using WeavePort.Abstractions;

namespace WeavePort.Hosting;

internal sealed partial class PluginSession(SessionBinding binding, TenantAdmission admission, WorkerPool pool, TimeProvider clock, Action<PluginSession, TenantAdmission> removed, ILogger logger) : IPluginOperationSession, IPluginFileSession
{
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

    internal async Task<InvocationResult> InvokeCoreAsync(string operation, JsonElement payload, bool streamExchange, FileExchange? files, CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();
        if (_disposed)
        {
            return Result(FailureCodes.Disabled, started, false);
        }

        if (InvocationScope.Current.Value is not null)
        {
            return new InvocationResult(FailureCodes.Denied, JsonSerializer.SerializeToElement(new
            {
            }), "", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        if (!await _gate.WaitAsync(0, CancellationToken.None))
        {
            RuntimeLog.AdmissionRejected(logger, "session-call");
            return Result(FailureCodes.Busy, started, false);
        }

        try
        {
            return await InvokeAdmittedAsync(operation, payload, started, streamExchange, files, cancellationToken);
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

    private async Task<InvocationResult> InvokeAdmittedAsync(string operation, JsonElement payload, long started, bool streamExchange, FileExchange? files, CancellationToken cancellationToken)
    {
        _dispatched = false;
        _clean = false;
        LastAcquisitionReused = false;
        bool admitted = false;
        string id = Guid.NewGuid().ToString("N");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        using Activity? activity = Traces.StartActivity("plugin.invoke");
        try
        {
            if (_disposed)
            {
                return Result(FailureCodes.Disabled, started, false);
            }

            if (files is not null && !MayAcceptFiles)
            {
                return Result(FailureCodes.FilesUnsupported, started, false);
            }

            if (!streamExchange)
            {
                deadline.CancelAfter(binding.Profile.Timeout ?? InvocationPolicy.DefaultTimeout);
            }

            admitted = await admission.Calls.WaitAsync(0, CancellationToken.None);
            if (!admitted)
            {
                RuntimeLog.AdmissionRejected(logger, "tenant-calls");
                return Result(FailureCodes.Busy, started);
            }

            deadline.Token.ThrowIfCancellationRequested();
            await EnsureStartedAsync(deadline.Token);
            return await ExecuteAsync(new InvokeFrame(FrameKinds.Invoke, id, operation, payload, binding.Context, activity?.TraceId.ToString() ?? id, files?.Descriptor), files, started, deadline.Token);
        }
        catch (WorkerCapacityException)
        {
            return Result(FailureCodes.Busy, started, false);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            string code = ClassifyFailure(error, cancellationToken, deadline.Token);
            bool filesCleanupFailed = error is OutputCleanupException || files is not null && await files.ReleaseAsync(FileExchange.AbortReason(code), logger, Instance);
            return await CompleteFailureAsync(error, code, id, started, activity, filesCleanupFailed);
        }
        finally
        {
            _files = null;
            if (files is not null)
            {
                await files.ReleaseAsync(null, logger, Instance);
            }

            if (admitted)
            {
                admission.Calls.Release();
            }
        }
    }

    // MCP bindings never take files; a binding whose worker already declined them is refused without starting another.
    // The same verified artifact backs every worker of a binding, so the observed capability holds across restarts.
    private bool MayAcceptFiles => binding.Profile is not ProcessProfile { Protocol: not ProcessProtocol.Native } && _filesSupported != false;

    // Runs a started invocation: refuses files a worker cannot take, dispatches, then commits outputs before reuse.
    private async Task<InvocationResult> ExecuteAsync(InvokeFrame frame, FileExchange? files, long started, CancellationToken token)
    {
        if (files is not null && _filesSupported != true)
        {
            return Result(FailureCodes.FilesUnsupported, started, false);
        }

        _files = files;
        JsonElement value = await DispatchAsync(frame, token);
        token.ThrowIfCancellationRequested();
        IReadOnlyList<PluginOutputInfo> outputs = files is null ? [] : await CommitFilesAsync(files, value, token);
        await ReturnCleanWorkerAsync();
        return new InvocationResult(FailureCodes.Ok, value, Instance, Stopwatch.GetElapsedTime(started).TotalMilliseconds, true)
        {
            Outputs = outputs
        };
    }

    // A result the caller cannot accept must not leave committed outputs behind, so the unary bound is checked first.
    // Writers are released before success is reported, so a failed release reaches the caller as cleanup-error.
    private async Task<IReadOnlyList<PluginOutputInfo>> CommitFilesAsync(FileExchange files, JsonElement value, CancellationToken token)
    {
        if (JsonSize.Measure(value) > ProtocolLimits.UnaryBytes)
        {
            throw new PluginFileException(FailureCodes.ValueLimit);
        }

        IReadOnlyList<PluginOutputInfo> outputs = await files.CommitAsync(token);
        return await files.ReleaseAsync(null, logger, Instance) ? throw new OutputCleanupException(outputs) : outputs;
    }

    private async Task<JsonElement> DispatchAsync(InvokeFrame frame, CancellationToken token)
    {
        InvocationScope? ownedScope = null;
        try
        {
            _termination = JsonSerializer.SerializeToElement(new
            {
            });
            ownedScope = new InvocationScope(binding.Context.Tenant, frame.TraceId, binding.Grants, token, binding.Profile.MaximumCallbacks);
            InvocationScope.Current.Value = ownedScope;
            _dispatched = true;
            if (_worker!.Mcp is { } mcp)
            {
                return await mcp.InvokeAsync(_worker, frame.Id, frame.Operation, frame.Payload, token);
            }

            await Frames.WriteAsync(_worker.Input, frame, WireJson.Default.InvokeFrame, token);
            return await ExchangeAsync(frame.Id, frame.TraceId, token);
        }
        finally
        {
            ownedScope?.Complete();
            InvocationScope.Current.Value = null;
        }
    }

    private async Task EnsureStartedAsync(CancellationToken token)
    {
        if (_worker is { Running: true })
        {
            return;
        }

        await StopAsync();
        _worker = await pool.AcquireAsync(binding.Profile, binding.Context.Version, binding.Context.Tenant, token, _freshNext);
        _filesSupported = _worker.SupportsFiles && _worker.Mcp is null;
        _freshNext = false;
        LastAcquisitionReused = _worker.AcquiredFromReuse;
        _instance = _worker.Instance;
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
        if (_operationGate.CurrentCount == 0 || binding.Profile.IdleTimeout is not { } idle || !await _gate.WaitAsync(0, token))
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
            _freshNext = true;
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
