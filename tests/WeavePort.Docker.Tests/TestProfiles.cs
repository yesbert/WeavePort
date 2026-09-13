using WeavePort.Hosting;

internal static class TestProfiles
{
    internal static UnixSocketTransport? SocketTransport => Environment.GetEnvironmentVariable("WEAVEPORT_TRANSPORT") == "socket"
        ? new UnixSocketTransport("/ipc", Environment.GetEnvironmentVariable("WEAVEPORT_SOCKET_DOCKER") ?? throw new ArgumentException("WEAVEPORT_SOCKET_DOCKER")) : null;

    internal static DockerProfile Create(string image, TimeSpan? timeout = null) => new(image, Timeout: timeout, SocketTransport: SocketTransport);
}
