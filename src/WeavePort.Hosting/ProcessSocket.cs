using System.Net.Sockets;

namespace WeavePort.Hosting;
internal sealed class ProcessSocket : IDisposable
{
    private readonly string _directory;
    private readonly Socket _listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    private bool _ownsDirectory;
    private int? _bufferBytes;
    internal string Path { get; }
    internal NetworkStream? Stream { get; private set; }

    internal ProcessSocket()
    {
        // macOS user temp paths plus UUID can exceed sockaddr_un's path capacity.
        string root = OperatingSystem.IsMacOS() ? "/tmp" : System.IO.Path.GetTempPath();
        _directory = System.IO.Path.Combine(root, "wp-" + Guid.NewGuid().ToString("N"));
        Path = System.IO.Path.Combine(_directory, "p.sock");
    }

    internal void Listen(int? bufferBytes = null)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException();
        }

        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException();
        }

        if (Directory.Exists(_directory))
        {
            throw new IOException("Native socket directory already exists.");
        }

        Directory.CreateDirectory(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        _ownsDirectory = true;
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
        if (_ownsDirectory && Directory.Exists(_directory))
        {
            File.Delete(Path);
            Directory.Delete(_directory);
        }
    }
}
