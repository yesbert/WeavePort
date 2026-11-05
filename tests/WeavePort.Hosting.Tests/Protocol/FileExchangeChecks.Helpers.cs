using System.Security.Cryptography;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;

internal static partial class FileExchangeChecks
{
    private static async Task<IPluginFileSession> BindAsync(PluginHost host, string root, bool advertise = true)
    {
        string executable = Environment.ProcessPath!;
        string mode = advertise ? "--file-worker" : "--file-worker-legacy";
        string[] arguments = Path.GetFileNameWithoutExtension(executable) == "dotnet" ? [typeof(FileExchangeChecks).Assembly.Location, mode] : [mode];
        var profile = new ProcessProfile(executable, arguments, trustedCode: true, workspaceRoot: Workspace, timeout: TimeSpan.FromSeconds(10), reservedMemoryMiB: 64);
        IPluginSession session = await host.BindAsync(new PluginContext("tenant", "files", "1", "test", Empty), profile, new NoCallbacks(), []);
        return (IPluginFileSession)session;
    }

    private static PluginInput Input(string name, byte[] bytes) => new(name, bytes.Length, "application/octet-stream", PluginInputReader.FromStream(new MemoryStream(bytes, writable: false)));

    private static JsonElement Script(params object[] steps) => JsonSerializer.SerializeToElement(new { steps });

    private static object Read(string name, long offset, int length) => new { operation = "$sdk.input.read", payload = new { name, offset, length } };

    private static object Create(string name, string mediaType) => new { operation = "$sdk.output.create", payload = new { name, mediaType } };

    private static object Write(string name, byte[] data) => new { operation = "$sdk.output.write", payload = new { name, data } };

    private static object Complete(string name) => new { operation = "$sdk.output.complete", payload = new { name } };

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static string Sha(JsonElement reply) => reply.GetProperty("sha").GetString()!;

    private static string Errors(InvocationResult result) => string.Join(",", result.Value.GetProperty("replies").EnumerateArray().Select(reply => reply.TryGetProperty("error", out JsonElement error) ? error.GetString() : ""));

    private static void Expect(bool condition, string label)
    {
        if (!condition)
        {
            throw new Exception("Function files: " + label);
        }

        _checks++;
    }

    private sealed class NoCallbacks : IHostCallbacks
    {
        public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken) => throw new UnauthorizedAccessException();
    }
}
