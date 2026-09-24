using WeavePort.Internal;
using System.Text.Json;

namespace WeavePort.Sdk;
/// <summary>Invocation-owned context and resources. Completed contexts reject callbacks and new registrations.</summary>
public sealed class PluginCallContext
{
    private readonly object _sync = new();
    private Func<string, JsonElement, CancellationToken, Task<JsonElement>>? _callback;
    private readonly List<Func<ValueTask>> _cleanup = [];
    private string? _tenant;
    private JsonElement _configuration;
    private bool _active = true;
    internal PluginCallContext(JsonElement context, Func<string, JsonElement, CancellationToken, Task<JsonElement>> callback)
    {
        _tenant = context.GetProperty(WireFields.Tenant).GetString()!;
        _configuration = context.GetProperty(WireFields.Configuration);
        _callback = callback;
    }

    /// <summary>Host-bound customer identity; unavailable after session cleanup.</summary>
    public string Tenant
    {
        get
        {
            lock (_sync)
            {
                EnsureActive();
                return _tenant!;
            }
        }
    }

    /// <summary>Configuration for this invocation or result stream; do not retain copies across sessions.</summary>
    public JsonElement Configuration
    {
        get
        {
            lock (_sync)
            {
                EnsureActive();
                return _configuration;
            }
        }
    }

    /// <summary>Registers cleanup before handing ownership to the session. Actions run in reverse order, including after handler failure.</summary>
    public void OnClose(Func<ValueTask> cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        lock (_sync)
        {
            EnsureActive();
            _cleanup.Add(cleanup);
        }
    }

    /// <summary>Transfers disposal ownership to this session. Do not separately dispose or retain the resource after completion.</summary>
    public T Own<T>(T resource)
        where T : IDisposable
    {
        ArgumentNullException.ThrowIfNull(resource);
        OnClose(() =>
        {
            resource.Dispose();
            return ValueTask.CompletedTask;
        });
        return resource;
    }

    /// <summary>Transfers asynchronous disposal ownership to this session.</summary>
    public T OwnAsync<T>(T resource)
        where T : IAsyncDisposable
    {
        ArgumentNullException.ThrowIfNull(resource);
        OnClose(resource.DisposeAsync);
        return resource;
    }

    /// <summary>Calls a capability while this session is active; the host independently checks authority.</summary>
    public Task<JsonElement> CallHostAsync(string operation, JsonElement input, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            EnsureActive();
            return _callback!(operation, input, cancellationToken);
        }
    }

    private void EnsureActive()
    {
        if (!_active)
        {
            throw new InvalidOperationException("Session completed.");
        }
    }

    internal async Task CompleteAsync()
    {
        Func<ValueTask>[] actions;
        lock (_sync)
        {
            _active = false;
            _callback = null;
            _tenant = null;
            _configuration = default;
            actions = _cleanup.ToArray();
            _cleanup.Clear();
        }

        List<Exception> errors = [];
        foreach (Func<ValueTask> action in actions.Reverse())
        {
            try
            {
                await action();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }

        if (errors.Count != 0)
        {
            throw new SessionCleanupException(errors);
        }
    }
}
