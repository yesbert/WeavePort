using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WeavePort.Abstractions;
using WeavePort.Hosting;

internal static class DisposalChecks
{
    internal static async Task RunAsync()
    {
        List<string> failures = [];
        var host = new PluginHost();
        var session = await host.BindAsync(new PluginContext("a", "test", "1", "test", JsonSerializer.SerializeToElement(new { })), new Profile(), new Callbacks(), []);
        var pool = new WorkerPool(new WorkerPoolOptions(), TimeProvider.System, NullLogger.Instance);
        foreach (var item in new (string Name, IAsyncDisposable Owner)[] { ("session", session), ("host", host), ("pool", pool) })
        {
            var source = (CancellationTokenSource)item.Owner.GetType().GetField("_lifetime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(item.Owner)!;
            await item.Owner.DisposeAsync();
            await item.Owner.DisposeAsync();
            try { AssertDisposed(source); }
            catch (Exception) { failures.Add(item.Name + " source not disposed"); }
        }
        if (failures.Count != 0) throw new Exception(string.Join("; ", failures));
        Console.WriteLine("PASS lifetime source disposal");
    }
    internal static CancellationTokenSource Source(object owner) =>
        (CancellationTokenSource)owner.GetType().GetField("_lifetime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;
    internal static void AssertDisposed(CancellationTokenSource source)
    {
        try { _ = source.Token; }
        catch (ObjectDisposedException) { return; }
        throw new Exception("Lifetime source not disposed");
    }
    private sealed record Profile() : ExecutionProfile(256, null, null)
    {
        public override ExecutionProtections Protection => ExecutionProtections.None;
        internal override Task<ExecutionProfile> ResolveAsync(CancellationToken token) => Task.FromResult<ExecutionProfile>(this);
        internal override ExecutionProfile Normalize() => this;
        internal override Worker CreateWorker(string version, TimeProvider clock) => throw new Exception("Must not start");
    }
    private sealed class Callbacks : IHostCallbacks
    {
        public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken) => throw new Exception("Must not call");
    }
}
