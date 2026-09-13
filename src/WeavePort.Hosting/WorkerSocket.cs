using System.Net.Sockets;

namespace WeavePort.Hosting;
internal sealed class WorkerSocket : IDisposable
{
    private readonly string _directory;
    private readonly string _path;
    private readonly Socket _listener;
    private Socket? _connection;
    internal NetworkStream Stream { get; private set; } = null!;

    internal WorkerSocket(UnixSocketTransport transport, string instance)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException();
        }

        transport.Validate();
        Directory.CreateDirectory(transport.LocalDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (new DirectoryInfo(transport.LocalDirectory).LinkTarget is not null || (File.GetUnixFileMode(transport.LocalDirectory) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
        {
            throw new IOException("Coordinator socket root must be a private, non-symlink directory.");
        }

        _directory = Path.Combine(transport.LocalDirectory, instance);
        _path = Path.Combine(_directory, "p.sock");
        if (Directory.Exists(_directory))
        {
            throw new IOException("Worker endpoint already exists.");
        }

        _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            Directory.CreateDirectory(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            _listener.Bind(new UnixDomainSocketEndPoint(_path));
            File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite);
            _listener.Listen(1);
        }
        catch
        {
            _listener.Dispose();
            RemoveEndpoint();
            throw;
        }
    }

    internal async Task AcceptAsync(CancellationToken token)
    {
        _connection = await _listener.AcceptAsync(token);
        Stream = new NetworkStream(_connection, ownsSocket: true);
        _listener.Dispose();
    }

    internal bool Running
    {
        get
        {
            if (_connection is null)
            {
                return false;
            }

            try
            {
                return !(_connection.Poll(0, SelectMode.SelectRead) && _connection.Available == 0);
            }
            catch (SocketException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }
    }

    // Only after Docker confirms removal. Non-recursive deletion never traverses plugin-created trees.
    internal void RemoveEndpoint()
    {
        if (!Directory.Exists(_directory))
        {
            return;
        }

        File.Delete(_path);
        Directory.Delete(_directory);
    }

    public void Dispose()
    {
        _listener.Dispose();
        Stream?.Dispose();
        _connection?.Dispose();
    }
}
