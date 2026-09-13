using System.Diagnostics;
using System.Text.Json.Nodes;

// Socket transport is selected only by the trusted launcher; default remains stdio.
if (Environment.GetEnvironmentVariable("WEAVEPORT_SOCKET") is { } endpoint)
{
    var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.Unix, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Unspecified);
    socket.Connect(new System.Net.Sockets.UnixDomainSocketEndPoint(endpoint));
    var stream = new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
    Console.SetIn(new StreamReader(stream, System.Text.Encoding.UTF8));
    Console.SetOut(new StreamWriter(stream, new System.Text.UTF8Encoding(false)) { AutoFlush = true });
}

string workspace = Path.Combine(Environment.GetEnvironmentVariable("WEAVEPORT_WORKSPACE") ?? "/tmp", "state");
int counter = 0;
int callbackId = 0;
Send(new { type = "ready", protocol = 1, pluginVersion = "1" });
while (Console.ReadLine() is { } line)
{
    JsonNode request = JsonNode.Parse(line)!;
    try
    {
        object? value = Execute(request);
        Send(new { type = "result", id = request["id"]!.GetValue<string>(), value });
    }
    catch (Exception error) when (error is not OutOfMemoryException)
    {
        Send(new { type = "error", id = request["id"]!.GetValue<string>(), code = "plugin-error" });
    }
}

object? Execute(JsonNode request)
{
    JsonNode p = request["payload"]!;
    switch (request["operation"]!.GetValue<string>())
    {
        case "environment": return new { parentSecretPresent = Environment.GetEnvironmentVariable("WEAVEPORT_PARENT_CANARY") is not null };
        case "trace": return request["traceId"];
        case "echo": return p;
        case "bulk-map":
            byte[] data = Convert.FromBase64String(p["data"]!.GetValue<string>());
            for (int i = 0; i < data.Length; i++) if (data[i] == (byte)'a') data[i] = (byte)'A';
            return new { data = Convert.ToBase64String(data) };
        case "context": return request["context"];
        case "counter": return ++counter;
        case "search":
            string query = p["query"]?.GetValue<string>() ?? "";
            return Callback(request, "documents.read", p)!.AsArray()
                .Where(d => d!["text"]!.GetValue<string>().Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d!["id"]!.GetValue<string>(), StringComparer.Ordinal).ToArray();
        case "reduce": return new { state = p["state"]!.GetValue<int>() + p["amount"]!.GetValue<int>(), events = new[] { new { kind = "changed", at = p["now"]!.GetValue<string>() } } };
        case "delay": Thread.Sleep(p["ms"]!.GetValue<int>()); return p;
        case "crash": Environment.Exit(17); return null;
        case "exception": throw new InvalidOperationException("deliberate failure");
        case "hang": while (true) { }
        case "memory":
            byte[] block = new byte[8 * 1024 * 1024];
            Array.Fill(block, (byte)42);
            while (true)
            {
                nint address = System.Runtime.InteropServices.Marshal.AllocHGlobal(block.Length);
                System.Runtime.InteropServices.Marshal.Copy(block, 0, address, block.Length);
            }
        case "oversize": return new string('x', 2 * 1024 * 1024);
        case "malformed": Console.WriteLine("not-json"); return null;
        case "callback": return Callback(request, p["operation"]!.GetValue<string>(), p["args"] ?? new JsonObject());
        case "callback-flood": for (int n = 0; n < (p["count"]?.GetValue<int>() ?? 10); n++) Callback(request, "documents.read", new JsonObject()); return null;
        case "workspace":
            if (p["text"] is { } text) File.WriteAllText(workspace, text.GetValue<string>());
            return File.Exists(workspace) ? File.ReadAllText(workspace) : "";
        case "subprocess":
            using (Process child = Process.Start(new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", "printf child-ok" }, RedirectStandardOutput = true })!)
            {
                string output = child.StandardOutput.ReadToEnd(); child.WaitForExit(); return output;
            }
        default: throw new InvalidOperationException("unknown operation");
    }
}

JsonNode? Callback(JsonNode request, string operation, JsonNode payload)
{
    string cid = (++callbackId).ToString(System.Globalization.CultureInfo.InvariantCulture);
    string id = request["id"]!.GetValue<string>();
    Send(new { type = "callback", id, callbackId = cid, operation, payload });
    JsonNode reply = JsonNode.Parse(Console.ReadLine()!)!;
    if (reply["id"]!.GetValue<string>() != id || reply["callbackId"]!.GetValue<string>() != cid) throw new InvalidOperationException("callback identity");
    return reply["value"];
}

static void Send(object value) => Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(value));
