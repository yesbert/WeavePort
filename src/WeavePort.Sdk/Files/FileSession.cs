using System.Text.Json;
using WeavePort.Internal;

namespace WeavePort.Sdk;
// Invocation-owned view of the host's file descriptors and the reserved file operations.
// Every operation travels through the runtime's serialized callback exchange.
internal sealed class FileSession
{
    internal delegate Task<(JsonElement Value, string? Error)> Exchange(string operation, object payload, CancellationToken token);

    private readonly Exchange _exchange;
    private readonly HashSet<string> _outputs = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private bool _closed;

    internal FileSession(JsonElement request, Exchange exchange, int blockBytes)
    {
        _exchange = exchange;
        BlockBytes = blockBytes;
        if (!request.TryGetProperty(WireFields.Files, out JsonElement files) || files.ValueKind != JsonValueKind.Object)
        {
            Inputs = [];
            return;
        }

        Inputs = files.TryGetProperty(WireFields.Inputs, out JsonElement inputs) ? [.. inputs.EnumerateArray().Select(Describe)] : [];
        OutputsPermitted = files.TryGetProperty(WireFields.Outputs, out JsonElement outputs) && outputs.ValueKind == JsonValueKind.Object;
    }

    internal IReadOnlyList<PluginFileInfo> Inputs { get; }
    internal bool OutputsPermitted { get; }
    internal int BlockBytes { get; }

    internal PluginFileInfo Input(string name) => Inputs.FirstOrDefault(input => input.Name == name) ?? throw new PluginFileException(FileErrors.InputNotFound);

    internal void ReserveOutput(string name, string mediaType)
    {
        FileDescriptor.Name(name, nameof(name));
        FileDescriptor.MediaType(mediaType, nameof(mediaType));
        lock (_sync)
        {
            EnsureOpen();
            if (!OutputsPermitted)
            {
                throw new PluginFileException(FileErrors.OutputNotPermitted);
            }

            if (!_outputs.Add(name))
            {
                throw new PluginFileException(FileErrors.OutputExists);
            }
        }
    }

    internal async Task<byte[]> ReadAsync(string name, long offset, CancellationToken token)
    {
        JsonElement value = await SendAsync(SdkOperations.InputRead, new
        {
            name,
            offset,
            length = BlockBytes
        }, token);
        return value.GetProperty(WireFields.Data).GetBytesFromBase64();
    }

    internal Task CreateAsync(string name, string mediaType, CancellationToken token) => SendAsync(SdkOperations.OutputCreate, new { name, mediaType }, token);

    internal Task WriteAsync(string name, ReadOnlyMemory<byte> data, CancellationToken token) => SendAsync(SdkOperations.OutputWrite, new { name, data }, token);

    internal async Task<long> CompleteAsync(string name, CancellationToken token) => (await SendAsync(SdkOperations.OutputComplete, new { name }, token)).GetProperty(WireFields.Length).GetInt64();

    internal void ThrowIfClosed()
    {
        lock (_sync)
        {
            EnsureOpen();
        }
    }

    internal void Close()
    {
        lock (_sync)
        {
            _closed = true;
        }
    }

    private async Task<JsonElement> SendAsync(string operation, object payload, CancellationToken token)
    {
        lock (_sync)
        {
            EnsureOpen();
        }

        (JsonElement value, string? error) = await _exchange(operation, payload, token);
        return error is null ? value : throw new PluginFileException(error);
    }

    private void EnsureOpen()
    {
        if (_closed)
        {
            throw new InvalidOperationException("Session completed.");
        }
    }

    private static PluginFileInfo Describe(JsonElement input) => new(
        input.GetProperty(WireFields.Name).GetString()!,
        input.GetProperty(WireFields.Length).GetInt64(),
        input.GetProperty(WireFields.MediaType).GetString()!);
}
