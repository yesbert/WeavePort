using Google.Protobuf;
using Grpc.Core;
using WeavePort.Abstractions;
using WeavePort.Internal;
using WeavePort.Sdk.Gateway.Protocol;

namespace WeavePort.Sdk.Gateway;
// Relays host reader and writer calls to the remote caller: one FileRequest out, exactly one matching FileAck in.
// The host never calls readers or writers of one invocation concurrently; the gate keeps that true regardless.
internal sealed class FileRelay(IAsyncStreamReader<Request> input, IServerStreamWriter<Reply> output, CancellationToken session)
{
    private readonly SemaphoreSlim _gate = new(1);
    private ulong _next;
    private bool _broken;

    internal bool Broken => _broken;

    internal void ThrowIfBroken()
    {
        if (_broken)
        {
            throw new FileRelayBrokenException();
        }
    }

    // Session I/O is bound only to the session's lifetime: cancelling a pending gRPC read would abort the whole call
    // and lose the invocation's own outcome. The host token only ends the wait; a late acknowledgement is discarded
    // with the session, which the gateway ends once a relay was interrupted.
    private async Task<FileAck> SendAsync(FileRequest request, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_broken)
            {
                throw new IOException("Caller file relay is no longer usable.");
            }

            request.Id = ++_next;
            _broken = true;
            await output.WriteAsync(new Reply { FileRequest = request }, session).WaitAsync(token);
            Task<bool> next = input.MoveNext(session);
            _ = next.ContinueWith(static task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            if (!await next.WaitAsync(token) || input.Current.FileAck is not { } ack || ack.Id != request.Id)
            {
                throw new InvalidDataException("Caller sent no matching file acknowledgement.");
            }

            _broken = false;
            return ack.Error.Length == 0 ? ack : throw new IOException("Caller file operation failed.");
        }
        finally
        {
            _gate.Release();
        }
    }

    internal sealed class Reader(FileRelay relay, string name) : IPluginInputReader
    {
        public async ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken)
        {
            FileAck ack = await relay.SendAsync(new FileRequest { Operation = FileOperation.FileRead, Name = name, Offset = offset, Length = buffer.Length }, cancellationToken);
            if (ack.Data.Length > buffer.Length)
            {
                relay._broken = true;
                throw new InvalidDataException("Caller returned more input bytes than requested.");
            }

            ack.Data.Span.CopyTo(buffer.Span);
            return ack.Data.Length;
        }
    }

    internal sealed class Destination(FileRelay relay) : IPluginOutputDestination
    {
        public async ValueTask<IPluginOutputWriter> CreateAsync(string name, string mediaType, CancellationToken cancellationToken)
        {
            await relay.SendAsync(new FileRequest { Operation = FileOperation.FileCreate, Name = name, MediaType = mediaType }, cancellationToken);
            return new Writer(relay, name);
        }
    }

    private sealed class Writer(FileRelay relay, string name) : IPluginOutputWriter
    {
        public async ValueTask WriteAsync(ReadOnlyMemory<byte> block, CancellationToken cancellationToken) => await relay.SendAsync(new FileRequest { Operation = FileOperation.FileWrite, Name = name, Data = ByteString.CopyFrom(block.Span) }, cancellationToken);
        public async ValueTask CommitAsync(CancellationToken cancellationToken) => await relay.SendAsync(new FileRequest { Operation = FileOperation.FileCommit, Name = name }, cancellationToken);
        public async ValueTask AbortAsync(PluginOutputAbort reason, CancellationToken cancellationToken) => await relay.SendAsync(new FileRequest { Operation = FileOperation.FileAbort, Name = name, AbortReason = reason.ToString() }, cancellationToken);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

// The session's request stream no longer lines up with the exchange; the gateway ends the session.
internal sealed class FileRelayBrokenException() : IOException("Caller file relay broke the session protocol.");
