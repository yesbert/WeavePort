using System.Net.Sockets;
using WeavePort.Hosting;

internal static class SocketChecks
{
    internal static async Task<int> RunAsync()
    {
        if (!OperatingSystem.IsLinux())
        {
            try { new UnixSocketTransport("/ipc", "/ipc").Validate(); }
            catch (PlatformNotSupportedException) { return 1; }
            throw new Exception("Non-Linux socket profile accepted");
        }
        int checks = 0;
        foreach (string path in new[] { "relative", "/bad,path", "/bad\npath", "/bad\0path", "/" + new string('x', 100) })
        {
            try { new UnixSocketTransport(path, "/daemon").Validate(); throw new Exception("Invalid path accepted"); }
            catch (ArgumentException) { checks++; }
        }
        string root = "/tmp/wp-" + Guid.NewGuid().ToString("N")[..12];
        var transport = new UnixSocketTransport(root, root);
        try
        {
            using (var listener = new WorkerSocket(transport, "waiting"))
            using (var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
            {
                try { await listener.AcceptAsync(deadline.Token); throw new Exception("Missing connection did not time out"); }
                catch (OperationCanceledException) { checks++; }
                listener.Dispose();
                listener.RemoveEndpoint();
            }
            using (var listener = new WorkerSocket(transport, "connected"))
            using (var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
            {
                await client.ConnectAsync(new UnixDomainSocketEndPoint(Path.Combine(root, "connected", "p.sock")), deadline.Token);
                await listener.AcceptAsync(deadline.Token);
                if (!listener.Running) throw new Exception("Accepted connection not live");
                client.Dispose();
                if (await listener.Stream.ReadAsync(new byte[1], deadline.Token) != 0 || listener.Running) throw new Exception("Peer exit not observed");
                checks++;
                listener.Dispose();
                listener.RemoveEndpoint();
            }
            if (Directory.GetFileSystemEntries(root).Length != 0) throw new Exception("Socket leaf leaked");
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherRead);
            try { using var rejected = new WorkerSocket(transport, "exposed"); throw new Exception("Shared root accepted"); }
            catch (IOException) { checks++; }
        }
        finally { Directory.Delete(root, recursive: true); }
        return checks;
    }
}
