using System.Runtime.CompilerServices;
using System.Text.Json;

namespace WeavePort.Sdk;
/// <summary>Registers plugin functions; the runtime owns transport, dispatch and stream batching.</summary>
public sealed class PluginApplication
{
    internal readonly Dictionary<string, Func<JsonElement, PluginCallContext, CancellationToken, ValueTask<JsonElement>>> Functions = [];
    internal readonly Dictionary<string, Func<JsonElement, PluginCallContext, CancellationToken, IAsyncEnumerable<JsonElement>>> Streams = [];
    internal readonly Dictionary<string, Func<JsonElement, PluginCallContext, CancellationToken, ValueTask<System.IO.Stream>>> Sources = [];
    private readonly JsonSerializerOptions _json;
    /// <summary>Opts into protocol 2 and concurrent unary invocations. Shared mutable state must be thread-safe.</summary>
    public bool ConcurrentCalls { get; init; }

    private string _pluginVersion = "1";
    /// <summary>Author-declared artifact version sent during startup; the host verifies its expected binding version.</summary>
    public string PluginVersion
    {
        get => _pluginVersion;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _pluginVersion = value;
        }
    }

    /// <summary>Creates an application with web JSON conventions or supplied serializer metadata/options.</summary>
    public PluginApplication(JsonSerializerOptions? json = null) => _json = json ?? new(JsonSerializerDefaults.Web);
    /// <summary>Registers an ordinary asynchronous function.</summary>
    public PluginApplication Function<TInput, TOutput>(string name, Func<TInput, PluginCallContext, CancellationToken, ValueTask<TOutput>> handler)
    {
        Validate(name);
        Functions.Add(name, async (input, context, token) => JsonSerializer.SerializeToElement(await handler(input.Deserialize<TInput>(_json)!, context, token), _json));
        return this;
    }

    /// <summary>Registers a lazy asynchronous stream; providers do not construct wire batches.</summary>
    public PluginApplication Stream<TInput, TItem>(string name, Func<TInput, PluginCallContext, CancellationToken, IAsyncEnumerable<TItem>> handler)
    {
        Validate(name);
        Streams.Add(name, (input, context, token) => SerializeAsync(handler(input.Deserialize<TInput>(_json)!, context, token), token));
        return this;
    }

    /// <summary>Registers a bounded byte source. The runtime disposes the returned stream when collection ends.</summary>
    public PluginApplication Source<TInput>(string name, Func<TInput, PluginCallContext, CancellationToken, ValueTask<System.IO.Stream>> handler)
    {
        Validate(name);
        Sources.Add(name, (input, context, token) => handler(input.Deserialize<TInput>(_json)!, context, token));
        return this;
    }

    private void Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('$') || Functions.ContainsKey(name) || Streams.ContainsKey(name) || Sources.ContainsKey(name))
        {
            throw new ArgumentException("Invalid or duplicate operation.", nameof(name));
        }
    }

    private async IAsyncEnumerable<JsonElement> SerializeAsync<T>(IAsyncEnumerable<T> items, [EnumeratorCancellation] CancellationToken token)
    {
        await foreach (T item in items.WithCancellation(token))
        {
            yield return JsonSerializer.SerializeToElement(item, _json);
        }
    }

    /// <summary>Runs the launcher-selected transport. Concurrent handlers receive invocation cancellation; the host retires workers that do not acknowledge termination.</summary>
    public Task RunAsync(CancellationToken cancellationToken = default) => ConcurrentCalls ? new ConcurrentRuntime(this).RunAsync(cancellationToken) : new Runtime(this).RunAsync(cancellationToken);
}
