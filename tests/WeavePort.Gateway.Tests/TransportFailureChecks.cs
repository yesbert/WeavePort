using System.Reflection;
using Grpc.Core;
using WeavePort.Sdk.Client;
using WeavePort.Sdk.Gateway;

internal static class TransportFailureChecks
{
    internal static void Run()
    {
        var translate = typeof(RemotePluginClient).GetMethod("Translate", BindingFlags.Static | BindingFlags.NonPublic)!;
        var cases = new Dictionary<StatusCode, string>
        {
            [StatusCode.Unavailable] = "unavailable", [StatusCode.DeadlineExceeded] = "timeout",
            [StatusCode.PermissionDenied] = "denied", [StatusCode.Unauthenticated] = "binding-denied",
            [StatusCode.ResourceExhausted] = "resource-exhausted", [StatusCode.Internal] = "gateway-failed",
            [StatusCode.Unknown] = "unknown-error"
        };
        foreach (var (status, code) in cases)
        {
            var rpc = new RpcException(new Status(status, "sensitive arbitrary description"));
            var actual = (PluginCallException)translate.Invoke(null, [rpc, CancellationToken.None])!;
            if (actual.ErrorCode != code || actual.Message.Contains("sensitive")) throw new Exception("Transport code depends on detail text.");
        }
        Console.WriteLine("PASS 7 fixed transport status mappings without sensitive descriptions");
    }
}
