using System.Buffers;
using Google.Protobuf;
using WeavePort.Abstractions;
using WeavePort.Internal;
using WeavePort.Sdk.Gateway.Protocol;

namespace WeavePort.Sdk.Gateway;
// Serves the gateway's file requests from the caller's own readers and writers. Every request is checked
// against the caller's declared inputs and limits, so a misbehaving server cannot read or write beyond them.
internal sealed class CallerFiles(PluginFiles files)
{
    private readonly Dictionary<string, PluginInput> _inputs = files.Inputs.ToDictionary(input => input.Name, StringComparer.Ordinal);
    private readonly List<Output> _outputs = [];
    private long _outputBytes;

    internal IReadOnlyList<PluginOutputInfo> Committed => [.. _outputs.Where(output => output.Committed).Select(output => new PluginOutputInfo(output.Name, output.MediaType, output.Length))];

    internal async Task<FileAck> HandleAsync(FileRequest request, CancellationToken token)
    {
        try
        {
            ByteString data = request.Operation switch
            {
                FileOperation.FileRead => await ReadAsync(request, token),
                FileOperation.FileCreate => await CreateAsync(request, token),
                FileOperation.FileWrite => await WriteAsync(request, token),
                FileOperation.FileCommit => await CommitAsync(request, token),
                FileOperation.FileAbort => await AbortAsync(request, token),
                _ => throw new InvalidDataException("Unknown gateway file operation.")
            };
            return new FileAck { Id = request.Id, Data = data };
        }
        catch (Exception error) when (error is not (OperationCanceledException or InvalidDataException or OutOfMemoryException))
        {
            // The caller's storage failed; the server fails the call without learning why.
            return new FileAck { Id = request.Id, Error = FailureCodes.Failed };
        }
    }

    private async Task<ByteString> ReadAsync(FileRequest request, CancellationToken token)
    {
        if (!_inputs.TryGetValue(request.Name, out PluginInput? input) || request.Offset < 0 || request.Length is < 0 or > ProtocolLimits.FileBlockMaximumBytes)
        {
            throw new InvalidDataException("Gateway requested an undeclared input or range.");
        }

        int count = (int)Math.Clamp(input.Length - request.Offset, 0, request.Length);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Max(count, 1));
        try
        {
            await PositionalRead.FillAsync(input.Reader, request.Offset, buffer.AsMemory(0, count), token);
            return ByteString.CopyFrom(buffer, 0, count);
        }
        finally
        {
            Array.Clear(buffer, 0, count);
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task<ByteString> CreateAsync(FileRequest request, CancellationToken token)
    {
        if (files.Outputs is null || !FileDescriptor.IsValidName(request.Name) || !FileDescriptor.IsValidMediaType(request.MediaType) || _outputs.Count >= files.Limits.MaximumOutputs || _outputs.Exists(output => output.Name == request.Name))
        {
            throw new InvalidDataException("Gateway created an output beyond the caller's grant.");
        }

        IPluginOutputWriter writer = await files.Outputs.CreateAsync(request.Name, request.MediaType, token);
        _outputs.Add(new Output(request.Name, request.MediaType, writer));
        return ByteString.Empty;
    }

    private async Task<ByteString> WriteAsync(FileRequest request, CancellationToken token)
    {
        Output output = Open(request.Name);
        if (request.Data.Length is 0 or > ProtocolLimits.FileBlockMaximumBytes || request.Data.Length > files.Limits.MaximumOutputBytes - _outputBytes)
        {
            throw new InvalidDataException("Gateway wrote beyond the caller's output limits.");
        }

        await output.Writer.WriteAsync(request.Data.Memory, token);
        _outputBytes += request.Data.Length;
        output.Length += request.Data.Length;
        return ByteString.Empty;
    }

    private async Task<ByteString> CommitAsync(FileRequest request, CancellationToken token)
    {
        Output output = Open(request.Name);
        await output.Writer.CommitAsync(token);
        output.Committed = true;
        return ByteString.Empty;
    }

    private async Task<ByteString> AbortAsync(FileRequest request, CancellationToken token)
    {
        // A failed commit leaves the output open, so the host's following abort still finds it.
        Output output = Open(request.Name);
        output.Aborted = true;
        await output.Writer.AbortAsync(Enum.TryParse(request.AbortReason, out PluginOutputAbort reason) ? reason : PluginOutputAbort.Failed, token);
        return ByteString.Empty;
    }

    private Output Open(string name) => _outputs.Find(output => output.Name == name && !output.Committed && !output.Aborted) ?? throw new InvalidDataException("Gateway used an unknown or finished output.");

    // Aborts outputs the exchange left open (transport failure, cancellation) and disposes every writer once; never throws.
    internal async Task ReleaseAsync(bool cancelled)
    {
        using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        foreach (Output output in _outputs)
        {
            await TryAsync(() => output.Committed || output.Aborted ? ValueTask.CompletedTask : output.Writer.AbortAsync(cancelled ? PluginOutputAbort.Cancelled : PluginOutputAbort.Failed, grace.Token));
            await TryAsync(() => output.Writer.DisposeAsync());
        }
    }

    private static async Task TryAsync(Func<ValueTask> action)
    {
        try
        {
            await action();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Release must not hide the call's own outcome; the destination owns its cleanup diagnostics.
        }
    }

    private sealed class Output(string name, string mediaType, IPluginOutputWriter writer)
    {
        internal string Name { get; } = name;
        internal string MediaType { get; } = mediaType;
        internal IPluginOutputWriter Writer { get; } = writer;
        internal long Length { get; set; }
        internal bool Committed { get; set; }
        internal bool Aborted { get; set; }
    }
}
