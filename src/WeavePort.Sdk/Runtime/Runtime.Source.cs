using System.Security.Cryptography;
using System.Text.Json;
using WeavePort.Internal;

namespace WeavePort.Sdk;
internal sealed partial class Runtime
{
    private Stream? _source;
    private string? _sourceId;
    private async Task<object> DispatchSourceAsync(string operation, JsonElement payload, CancellationToken token)
    {
        if (operation == SdkOperations.SourceOpen)
        {
            return await OpenSourceAsync(payload, token);
        }

        if (_source is null || payload.GetProperty(WireFields.Source).GetString() != _sourceId)
        {
            throw new InvalidDataException("Source ownership.");
        }

        if (operation == SdkOperations.SourceClose)
        {
            await CloseAsync();
            return new
            {
            };
        }

        int chunkBytes = payload.GetProperty(WireFields.ChunkBytes).GetInt32();
        if (chunkBytes is < ProtocolLimits.SourceChunkMinimumBytes or > ProtocolLimits.SourceChunkMaximumBytes)
        {
            throw new InvalidDataException("Source chunk limit.");
        }

        byte[] buffer = new byte[chunkBytes];
        try
        {
            int count = await _source.ReadAsync(buffer, token);
            bool done = count == 0;
            string data = Convert.ToBase64String(buffer, 0, count);
            if (done)
            {
                await CloseAsync();
            }

            return new
            {
                data,
                done
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private async Task CloseSourceAsync()
    {
        Stream? source = _source;
        _source = null;
        _sourceId = null;
        if (source is not null)
        {
            try
            {
                await source.DisposeAsync();
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                throw new SessionCleanupException([error]);
            }
        }
    }

    private async Task<object> OpenSourceAsync(JsonElement payload, CancellationToken token)
    {
        if (_source is not null || _enumerator is not null)
        {
            throw new InvalidOperationException("A source or stream is already active.");
        }

        _sessionScope = _scope.Value;
        var context = _context = new PluginCallContext(_request.GetProperty(WireFields.Context), CallbackAsync);
        _source = await application.Sources[payload.GetProperty(WireFields.Operation).GetString()!](payload.GetProperty(WireFields.Input), context, token);
        if (!_source.CanRead)
        {
            throw new InvalidOperationException("Source must be readable.");
        }

        _sourceId = Guid.NewGuid().ToString("N");
        return new
        {
            source = _sourceId
        };
    }
}
