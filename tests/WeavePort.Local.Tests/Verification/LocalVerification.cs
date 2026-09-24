using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.LocalTools;
using WeavePort.Testing;

internal static partial class LocalVerification
{
    internal static Task<int> DoctorAsync(LocalConfiguration config, string output) => LocalDoctor.RunAsync(config, output);

    internal static async Task<int> RunAsync(LocalConfiguration config, string output)
    {
        if (await DoctorAsync(config, output) != 0)
        {
            return 1;
        }
        return await new Scenario(config, output).RunAsync();
    }

    internal static PluginContext Context(string tenant) => new(tenant, "demo", "1", "default", JsonSerializer.SerializeToElement(new { marker = tenant + "-canary" }));
    internal static async Task<JsonElement> CallAsync(IPluginSession session, string operation, object payload) => ContractChecks.Successful(await session.InvokeAsync(operation, JsonSerializer.SerializeToElement(payload)));
    private static void Assert(bool condition)
    {
        if (!condition)
        {
            throw new Exception("Local contract assertion failed");
        }
    }
    internal sealed class Callbacks : IHostCallbacks
    {
        public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (call.Operation != "documents.read" || call.Payload.TryGetProperty("tenant", out JsonElement claimed) && claimed.GetString() != call.Context.Tenant)
            {
                throw new UnauthorizedAccessException();
            }

            return ValueTask.FromResult(JsonSerializer.SerializeToElement(new[] { new { id = call.Context.Tenant + "-1", text = "WeavePort document" } }));
        }
    }
}
