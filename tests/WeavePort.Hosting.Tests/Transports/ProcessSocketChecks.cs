using WeavePort.Hosting;
using System.Net.Sockets;

internal static class ProcessSocketChecks
{
    internal static async Task<int> RunAsync()
    {
        if (OperatingSystem.IsWindows() || (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux()))
        {
            return 0;
        }

        int checks = 0;
        using var endpoint = new ProcessSocket();
        if (!Directory.Exists(Path.GetDirectoryName(endpoint.Path)))
        {
            throw new Exception("Endpoint directory was not exclusively allocated during construction");
        }

        CheckUnstartedEndpoint(endpoint.Path);
        endpoint.Listen();
        string directory = Path.GetDirectoryName(endpoint.Path)!;
        if (File.GetUnixFileMode(directory) != (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute))
        {
            throw new Exception("Private directory mode");
        }

        if (File.GetUnixFileMode(endpoint.Path) != (UnixFileMode.UserRead | UnixFileMode.UserWrite))
        {
            throw new Exception("Private socket mode");
        }

        checks += 2;
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        try
        {
            await endpoint.AcceptAsync(cancel.Token);
            throw new Exception("Expected cancelled accept");
        }
        catch (OperationCanceledException) { checks++; }
        string marker = Path.Combine(directory, "cleanup-obstruction");
        await File.WriteAllTextAsync(marker, "synthetic");
        try
        {
            endpoint.Dispose();
            throw new Exception("Uncertain cleanup must fail");
        }
        catch (IOException) { checks++; }
        File.Delete(marker);
        endpoint.Dispose();
        if (Directory.Exists(directory))
        {
            throw new Exception("Endpoint retained after cleanup retry");
        }

        checks++;
        await CheckBufferConfigurationAsync();
        return checks + 1;
    }
    private static async Task CheckBufferConfigurationAsync()
    {
        using var buffered = new ProcessSocket();
        buffered.Listen(262144);
        using var peer = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await peer.ConnectAsync(new UnixDomainSocketEndPoint(buffered.Path));
        await buffered.AcceptAsync(CancellationToken.None);
        if (buffered.Stream!.Socket.SendBufferSize < 262144 || buffered.Stream.Socket.ReceiveBufferSize < 262144)
        {
            throw new Exception("Host buffer request not effective");
        }

    }
    private static void CheckUnstartedEndpoint(string existingPath)
    {
        using (var unused = new ProcessSocket())
        {
            if (unused.Path == existingPath)
            {
                throw new Exception("Shared endpoint directory");
            }

            string unusedDirectory = Path.GetDirectoryName(unused.Path)!;
            unused.Dispose();
            if (Directory.Exists(unusedDirectory))
            {
                throw new Exception("Unstarted endpoint retained");
            }
        }
    }

}
