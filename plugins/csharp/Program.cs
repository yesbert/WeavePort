// Socket transport is selected only by the trusted launcher; default remains stdio.
if (Environment.GetEnvironmentVariable("WEAVEPORT_SOCKET") is { } endpoint)
{
    var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.Unix, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Unspecified);
    socket.Connect(new System.Net.Sockets.UnixDomainSocketEndPoint(endpoint));
    var stream = new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
    Console.SetIn(new StreamReader(stream, System.Text.Encoding.UTF8));
    Console.SetOut(new StreamWriter(stream, new System.Text.UTF8Encoding(false)) { AutoFlush = true });
}


new FixtureRuntime().Run();
