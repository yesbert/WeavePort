using System.Net.Sockets;

namespace WeavePort.Hosting;

internal sealed class ProcessSocket : IDisposable
{
    private readonly string _directory;
    private readonly Socket _listener;
    private int? _bufferBytes;
    internal string Path { get; }
    internal NetworkStream? Stream { get; private set; }

    internal ProcessSocket()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException();
        }

        _listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            _directory = Directory.CreateTempSubdirectory("wp-").FullName;
        }
        catch
        {
            _listener.Dispose();
            throw;
        }

        Path = System.IO.Path.Combine(_directory, "p.sock");
    }

    internal void Listen(int? bufferBytes = null)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException();
        }

        _bufferBytes = bufferBytes;
        Configure(_listener);
        _listener.Bind(new UnixDomainSocketEndPoint(Path));
        File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        _listener.Listen(1);
    }

    internal async Task AcceptAsync(CancellationToken token)
    {
        Socket connection = await _listener.AcceptAsync(token);
        Stream = new NetworkStream(connection, ownsSocket: true);
        Configure(connection);
        _listener.Dispose();
    }

    private void Configure(Socket socket)
    {
        if (_bufferBytes is not { } bytes)
        {
            return;
        }

        socket.SendBufferSize = bytes;
        socket.ReceiveBufferSize = bytes;
    }

    public void Dispose()
    {
        _listener.Dispose();
        Stream?.Dispose();
        if (Directory.Exists(_directory))
        {
            File.Delete(Path);
            Directory.Delete(_directory);
        }
    }
}
