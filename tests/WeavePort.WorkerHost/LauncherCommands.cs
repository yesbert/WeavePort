using System.Text.Json;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;
using WeavePort.SdkFixture;

internal static class LauncherCommands
{
    internal const int MaximumMessageBytes = 1 << 20;

    internal static async Task WarmupAsync(LocalPluginClient[] clients)
    {
        foreach (var client in clients)
        {
            await client.CallAsync("echo", JsonSerializer.SerializeToElement(new
            {
            }));
        }
    }

    internal static void WriteDiagnostics(PluginHost host, LocalPluginClient[] clients)
    {
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            snapshot = host.Snapshot,
            workers = Fixture.WorkerIds(clients),
            managedBytes = GC.GetTotalMemory(false),
            allocatedBytes = GC.GetTotalAllocatedBytes(),
            gen0 = GC.CollectionCount(0),
            gen1 = GC.CollectionCount(1),
            gen2 = GC.CollectionCount(2),
            handles = current.HandleCount
        }));
    }
}
