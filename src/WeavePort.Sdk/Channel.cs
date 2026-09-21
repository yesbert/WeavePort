using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace WeavePort.Sdk;
internal sealed class Channel : IAsyncDisposable
{
    private readonly SemaphoreSlim _writeGate = new(1);
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly StreamReader _reader;
    internal Channel()
    {
        if (Environment.GetEnvironmentVariable("WEAVEPORT_SOCKET")is { } endpoint)
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                socket.Connect(new UnixDomainSocketEndPoint(endpoint));
            }
            catch
            {
                socket.Dispose();
                throw;
            }

            _input = _output = new NetworkStream(socket, ownsSocket: true);
        }
        else
        {
            _input = Console.OpenStandardInput();
            _output = Console.OpenStandardOutput();
        }

        Console.SetOut(Console.Error);
        _reader = new StreamReader(_input, Encoding.UTF8, false, 65536, leaveOpen: true);
    }

    internal async Task<JsonElement?> ReadAsync(CancellationToken token)
    {
        string? line = await _reader.ReadLineAsync(token);
        if (line is null)
        {
            return null;
        }

        if (Encoding.UTF8.GetByteCount(line) > 1 << 20)
        {
            throw new InvalidDataException("Frame limit.");
        }

        return JsonElement.Parse(line);
    }

    internal async Task WriteAsync(object value, CancellationToken token)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > 1 << 20)
        {
            throw new InvalidDataException("Frame limit.");
        }

        await _writeGate.WaitAsync(token);
        try
        {
            await _output.WriteAsync(bytes, token);
            await _output.WriteAsync("\n"u8.ToArray(), token);
            await _output.FlushAsync(token);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _reader.Dispose();
        await _input.DisposeAsync();
        if (_output != _input)
        {
            await _output.DisposeAsync();
        }
    }
}
