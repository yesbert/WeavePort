using System.Buffers;
using WeavePort.Internal;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace WeavePort.Hosting;

internal sealed class Frames(Stream input) : IDisposable
{
    private const int RetainedBufferBytes = 4096;
    private static readonly ActivitySource Diagnostics = new("WeavePort.Transport");
    internal const int MaximumJsonDepth = 32;
    internal const int MaximumBytes = ProtocolLimits.FrameBytes;
    private byte[] _buffer = ArrayPool<byte>.Shared.Rent(RetainedBufferBytes);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private int _start;
    private int _end;
    internal static async Task WriteAsync<T>(Stream output, T value, JsonTypeInfo<T> type, CancellationToken token)
    {
        using Activity? activity = Diagnostics.StartActivity("frame.write");
        long started = activity is null ? 0 : Stopwatch.GetTimestamp();
        using var buffer = new FrameBuffer();
        await JsonSerializer.SerializeAsync(buffer, value, type, token);
        activity?.SetTag("serialize.ms", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        activity?.SetTag("bytes", buffer.WrittenMemory.Length + 1);
        await output.WriteAsync(buffer.WrittenMemory, token);
        await output.WriteAsync(Newline, token);
        await output.FlushAsync(token);
        activity?.SetTag("complete.ms", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    private static readonly byte[] Newline = [(byte)'\n'];
    internal async Task<JsonElement> ReadAsync(CancellationToken token)
    {
        using Activity? activity = Diagnostics.StartActivity("frame.read");
        long started = activity is null ? 0 : Stopwatch.GetTimestamp();
        int reads = 0;
        int scanned = 0;
        while (true)
        {
            int delimiter = _buffer.AsSpan(_start + scanned, _end - _start - scanned).IndexOf((byte)'\n');
            if (delimiter >= 0)
            {
                delimiter += scanned;
                ValidateEncoding(delimiter);
                long parseStarted = activity is null ? 0 : Stopwatch.GetTimestamp();
                JsonElement result = JsonElement.Parse(_buffer.AsSpan(_start, delimiter), new JsonDocumentOptions { MaxDepth = MaximumJsonDepth });
                activity?.SetTag("parse.ms", Stopwatch.GetElapsedTime(parseStarted).TotalMilliseconds);
                activity?.SetTag("bytes", delimiter + 1);
                activity?.SetTag("reads", reads);
                activity?.SetTag("complete.ms", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                Consume(delimiter + 1);
                return result;
            }

            int remaining = _end - _start;
            scanned = remaining;
            if (remaining > MaximumBytes)
            {
                throw new InvalidDataException("Frame exceeds limit.");
            }

            PrepareReadBuffer(remaining);
            int available = Math.Min(_buffer.Length - _end, MaximumBytes + 1 - _end);
            int count = await input.ReadAsync(_buffer.AsMemory(_end, available), token);
            if (count == 0)
            {
                throw new EndOfStreamException("Plugin exited before returning a frame.");
            }

            _end += count;
            if (activity is null || reads++ != 0)
            {
                continue;
            }

            activity.SetTag("first-byte.ms", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    private void PrepareReadBuffer(int remaining)
    {
        if (_start != 0)
        {
            _buffer.AsSpan(_start, remaining).CopyTo(_buffer);
            CryptographicOperations.ZeroMemory(_buffer.AsSpan(remaining, _end - remaining));
            _start = 0;
            _end = remaining;
        }

        if (_end == _buffer.Length)
        {
            Resize(Math.Min(_buffer.Length * 2, MaximumBytes + 1));
        }
    }

    private void Consume(int count)
    {
        CryptographicOperations.ZeroMemory(_buffer.AsSpan(_start, count));
        _start += count;
        if (_start != _end)
        {
            return;
        }

        _start = _end = 0;
        if (_buffer.Length > RetainedBufferBytes)
        {
            Resize(RetainedBufferBytes);
        }
    }

    private void ValidateEncoding(int delimiter)
    {
        if (delimiter > MaximumBytes)
        {
            throw new InvalidDataException("Frame exceeds limit.");
        }

        try
        {
            StrictUtf8.GetCharCount(_buffer.AsSpan(_start, delimiter));
        }
        catch (DecoderFallbackException error)
        {
            throw new InvalidDataException("Invalid UTF-8 frame.", error);
        }
    }

    private void Resize(int size)
    {
        byte[] previous = _buffer;
        _buffer = ArrayPool<byte>.Shared.Rent(size);
        previous.AsSpan(_start, _end - _start).CopyTo(_buffer);
        _end -= _start;
        _start = 0;
        CryptographicOperations.ZeroMemory(previous);
        ArrayPool<byte>.Shared.Return(previous);
    }

    public void Dispose()
    {
        byte[] buffer = _buffer;
        _buffer = [];
        CryptographicOperations.ZeroMemory(buffer);
        if (buffer.Length != 0)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
