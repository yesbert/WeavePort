using WeavePort.Internal;
using System.Security.Cryptography;

namespace WeavePort.Composition;
/// <summary>Private, request-owned immutable file results. The trusted product supplies tenant identity and a private storage root.</summary>
public sealed class ResultScope : IAsyncDisposable
{
    private const int CopyBufferBytes = 65536;
    private readonly string _directory;
    private readonly ResultLimits _limits;
    private readonly object _sync = new();
    private readonly Dictionary<string, ResultHandle> _results = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _active;
    private int _objects;
    private long _bytes;
    private bool _closed;
    private Task? _disposal;
    /// <summary>Creates a new request scope under a trusted, host-private root; plugins must not be given access to that root.</summary>
    public ResultScope(string privateRoot, string tenant, ResultLimits? limits = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        Tenant = tenant;
        _limits = limits ?? new();
        if (_limits.MaximumObjectBytes <= 0 || _limits.MaximumScopeBytes <= 0 || _limits.MaximumObjects <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limits));
        }

        _directory = Path.Combine(Path.GetFullPath(privateRoot), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>Gets host-supplied identity; plugin payloads must never determine this value.</summary>
    public string Tenant { get; }

    /// <summary>Gets committed and in-progress byte reservations.</summary>
    public long ReservedBytes
    {
        get
        {
            lock (_sync)
            {
                return _bytes;
            }
        }
    }

    private Operation Enter()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            _active++;
            return new Operation(this);
        }
    }

    private sealed class Operation(ResultScope owner) : IDisposable
    {
        public void Dispose()
        {
            lock (owner._sync)
            {
                if (--owner._active == 0 && owner._closed)
                {
                    owner._idle.TrySetResult();
                }
            }
        }
    }

    private FileStream CreateResultFile(string id) => OpenResultFile(id, FileMode.CreateNew, FileAccess.Write);
    private FileStream ReadResultFile(string id) => OpenResultFile(id, FileMode.Open, FileAccess.Read);
    private FileStream OpenResultFile(string id, FileMode mode, FileAccess access)
    {
        var options = new FileStreamOptions
        {
            Mode = mode,
            Access = access,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            BufferSize = CopyBufferBytes
        };
        return new FileStream(Path.Combine(_directory, id), options);
    }

    private string Resolve(ResultHandle handle)
    {
        lock (_sync)
        {
            if (!_results.TryGetValue(handle.Id, out ResultHandle? found) || !ReferenceEquals(found, handle))
            {
                throw new UnauthorizedAccessException("Result does not belong to this request.");
            }

            return handle.Id;
        }
    }

    /// <summary>Produces a result atomically. The producer must await its writes and must not retain the supplied stream.</summary>
    public async Task<ResultHandle> CreateAsync(Func<Stream, CancellationToken, Task> producer, CancellationToken cancellationToken = default)
    {
        using var active = Enter();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        lock (_sync)
        {
            if (_objects >= _limits.MaximumObjects)
            {
                throw new IOException("Result object quota exceeded.");
            }

            _objects++;
        }

        string id = Guid.NewGuid().ToString("N");
        long reserved = 0;
        try
        {
            await using (FileStream file = CreateResultFile(id))
            {
                using var limited = new BoundedOutput(file, count =>
                {
                    lock (_sync)
                    {
                        if (count > _limits.MaximumObjectBytes - reserved || count > _limits.MaximumScopeBytes - _bytes)
                        {
                            throw new IOException("Result byte quota exceeded.");
                        }

                        reserved += count;
                        _bytes += count;
                    }
                }, linked.Token);
                await producer(limited, linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                await file.FlushAsync(linked.Token);
                if (file.Length != reserved)
                {
                    throw new IOException("Producer writes did not complete consistently.");
                }
            }

            var handle = new ResultHandle(id, reserved);
            lock (_sync)
            {
                _results.Add(id, handle);
            }

            return handle;
        }
        catch (Exception productionError)
        {
            RemovePartial(id, productionError);
            lock (_sync)
            {
                _bytes -= reserved;
                _objects--;
            }

            throw;
        }
    }

    private void RemovePartial(string id, Exception productionError)
    {
        try
        {
            File.Delete(Path.Combine(_directory, id));
        }
        catch (Exception cleanupError)
        {
            throw new AggregateException(productionError, cleanupError);
        }
    }

    /// <summary>Streams an authorized immutable result to a caller-owned destination with bounded buffering.</summary>
    public async Task CopyToAsync(ResultHandle handle, Stream destination, CancellationToken cancellationToken = default)
    {
        using var active = Enter();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await using FileStream source = ReadResultFile(Resolve(handle));
        await source.CopyToAsync(destination, CopyBufferBytes, linked.Token);
    }

    /// <summary>Transforms one bounded chunk at a time. The transform must not retain its input memory after returning.</summary>
    public async Task<ResultHandle> TransformAsync(ResultHandle input, Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<ReadOnlyMemory<byte>>> transform, int chunkBytes = ProtocolLimits.SourceChunkDefaultBytes, CancellationToken cancellationToken = default)
    {
        if (chunkBytes is < ProtocolLimits.SourceChunkMinimumBytes or > ProtocolLimits.SourceChunkMaximumBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkBytes));
        }

        using var active = Enter();
        string id = Resolve(input);
        return await CreateAsync(async (output, token) =>
        {
            await using FileStream source = ReadResultFile(id);
            byte[] buffer = new byte[chunkBytes];
            try
            {
                int count;
                while ((count = await source.ReadAsync(buffer, token)) != 0)
                {
                    ReadOnlyMemory<byte> result = await transform(buffer.AsMemory(0, count), token);
                    if (result.Length > chunkBytes)
                    {
                        throw new InvalidDataException("Transformed chunk exceeds bound.");
                    }

                    await output.WriteAsync(result, token);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(buffer);
            }
        }, cancellationToken);
    }

    /// <summary>Cancels operations, waits for their owners to release I/O and removes all request results. Cooperative producers must observe cancellation.</summary>
    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_disposal is not null)
            {
                return new(_disposal);
            }

            _closed = true;
            if (_active == 0)
            {
                _idle.TrySetResult();
            }

            _disposal = DisposeCoreAsync();
            return new(_disposal);
        }
    }

    private async Task DisposeCoreAsync()
    {
        var errors = new List<Exception>();
        try
        {
            await _lifetime.CancelAsync();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }

        await _idle.Task;
        try
        {
            Directory.Delete(_directory, true);
            lock (_sync)
            {
                _results.Clear();
                _bytes = 0;
                _objects = 0;
            }
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        finally
        {
            _lifetime.Dispose();
        }

        if (errors.Count > 0)
        {
            throw new AggregateException(errors).Flatten();
        }
    }
}
