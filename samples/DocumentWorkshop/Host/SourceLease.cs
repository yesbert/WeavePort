using System.Text.Json;
using DocumentWorkshop.Contracts;
using WeavePort.Abstractions;

namespace DocumentWorkshop.Host;
internal sealed class SourceLease(string path, string tenant, string profile, DocumentSource source) : IHostCallbacks, IAsyncDisposable
{
    private readonly FileStream _file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    private readonly SemaphoreSlim _gate = new(1);
    private bool _disposed;
    private long _transferred;
    internal int Calls { get; private set; }
    internal int MaximumRead { get; private set; }
    internal bool Revoked => _disposed;

    public async ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_disposed || call.Operation != "document.read" || call.Context.Tenant != tenant || call.Context.Profile != profile)
            {
                throw new UnauthorizedAccessException("Document lease scope refused.");
            }

            var request = call.Payload.Deserialize<ReadRequest>(JsonSerializerOptions.Web) ?? throw new InvalidDataException("Missing read request.");
            if (request.DocumentId != source.Id || request.Offset < 0 || request.Offset > source.Length || request.Count is <= 0 or > Limits.ReadBytes || Calls >= 1024)
            {
                throw new UnauthorizedAccessException("Invalid document read.");
            }

            int count = (int)Math.Min(request.Count, source.Length - request.Offset);
            if (_transferred + count > source.Length + Limits.ReadBytes)
            {
                throw new UnauthorizedAccessException("Document transfer budget exceeded.");
            }

            var bytes = new byte[count];
            _file.Position = request.Offset;
            await _file.ReadExactlyAsync(bytes, token);
            Calls++;
            _transferred += count;
            MaximumRead = Math.Max(MaximumRead, count);
            return JsonSerializer.SerializeToElement(new ReadReply(request.Offset, bytes, request.Offset + count == source.Length), JsonSerializerOptions.Web);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!_disposed)
            {
                _disposed = true;
                await _file.DisposeAsync();
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
