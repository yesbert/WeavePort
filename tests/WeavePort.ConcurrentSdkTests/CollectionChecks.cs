using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WeavePort.Abstractions;
using WeavePort.Composition;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;
using WeavePort.Sdk.Gateway;

internal static class CollectionChecks
{
    private static readonly JsonSerializerOptions ReportJson = new() { WriteIndented = true };
    internal static async Task RunAsync(string[] args)
    {
        string root = Path.Combine(Path.GetTempPath(), "wp-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var rows = new List<object>();
        try
        {
            foreach (string language in new[] { "csharp", "python", "typescript" })
            foreach (bool remote in new[] { false, true })
            {
                await using var host = new PluginHost(new SchedulingOptions { MaximumWorkers = 2, MaximumHeavyCalls = 0, MemoryBudgetMiB = 1024, MaximumPristineWorkers = 0 });
                var profile = language == "csharp" ? new ProcessProfile(Environment.ProcessPath!, [typeof(CollectionChecks).Assembly.Location], true, Path.Combine(root, "workers")) : StreamChecks.Profile(Path.GetFullPath(args.ElementAtOrDefault(1) ?? Environment.CurrentDirectory), language) with { WorkspaceRoot = Path.Combine(root, "workers") };
                var context = new PluginContext("source-owner", "source-test", "1", "native", JsonSerializer.SerializeToElement(new { }));
                var callbacks = new Callbacks();
                var session = await host.BindAsync(context, profile, callbacks, ["echo"]);
                await using var local = new LocalPluginClient(session);
                var builder = WebApplication.CreateSlimBuilder();
                builder.Logging.ClearProviders();
                builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2));
                builder.Services.AddWeavePortGateway();
                await using var app = builder.Build();
                app.MapWeavePortGateway();
                await app.StartAsync();
                var registry = app.Services.GetRequiredService<GatewayRegistry>();
                string credential = registry.Register(local);
                await using var gateway = new RemotePluginClient(new Uri(app.Urls.Single()), credential);
                IBoundPluginClient client = remote ? gateway : local;
                // Warm startup before measuring complete source collection and integrity delivery.
                await client.CallAsync("identity", JsonSerializer.SerializeToElement(new { delay = 0 }));
                long started = Stopwatch.GetTimestamp();
                await using (var scope = new ResultScope(Path.Combine(root, "results"), "source-owner"))
                {
                    int workerPid = (await client.CallAsync("stats", JsonSerializer.SerializeToElement(new { }))).GetProperty("pid").GetInt32();
                    await using var memory = new MemorySampler(workerPid);
                    long before = GC.GetTotalAllocatedBytes(true);
                    ResultHandle result = await Composition.CollectAsync(scope, client, "large", JsonSerializer.SerializeToElement(new { bytes = 200L * 1024 * 1024 }), 262144);
                    double collectMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    using var sink = new CheckingSink();
                    await scope.CopyToAsync(result, sink);
                    Assert(sink.Bytes == 200L * 1024 * 1024 && result.Length == sink.Bytes, "Full 200MiB source integrity");
                    rows.Add(new { language, remote, bytes = sink.Bytes, collectMs, deliveredMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds, hostAllocatedBytes = GC.GetTotalAllocatedBytes(true) - before, memory.HostPeakBytes, memory.WorkerPeakBytes, memory.HostInitialBytes, memory.WorkerInitialBytes });
                }

                await using (var quota = new ResultScope(Path.Combine(root, "results"), "source-owner", new ResultLimits(100000, 100000, 2)))
                {
                    try
                    {
                        await Composition.CollectAsync(quota, client, "large", JsonSerializer.SerializeToElement(new { bytes = 200L * 1024 * 1024 }));
                        throw new InvalidOperationException("Quota was not enforced.");
                    }
                    catch (IOException) { }
                    Assert(quota.ReservedBytes == 0, "Partial quota reservation removed");
                }

                await using (var scope = new ResultScope(Path.Combine(root, "results"), "source-owner"))
                using (var stop = new CancellationTokenSource(30))
                {
                    try
                    {
                        await Composition.CollectAsync(scope, client, "large", JsonSerializer.SerializeToElement(new { bytes = 200L * 1024 * 1024 }), cancellationToken: stop.Token);
                        throw new InvalidOperationException("Cancellation was not observed.");
                    }
                    catch (OperationCanceledException) { }
                    Assert(scope.ReservedBytes == 0, "Cancelled partial reservation removed");
                }

                await client.CallAsync("identity", JsonSerializer.SerializeToElement(new { delay = 0 }));
                started = Stopwatch.GetTimestamp();
                var received = new List<int>();
                using var liveDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await foreach (var item in client.StreamAsync("immediateCallback", JsonSerializer.SerializeToElement(new { }), liveDeadline.Token))
                {
                    if (received.Count == 0)
                    {
                        callbacks.Release.TrySetResult();
                    }
                    received.Add(item.GetInt32());
                }
                Assert(received.SequenceEqual([1, 2]), "Live stream results");
                if (remote)
                {
                    await RemotePauseChecks.RunAsync(host, gateway, new Uri(app.Urls.Single()), credential);
                }
                await gateway.DisposeAsync();
                await app.StopAsync();
            }

            Console.WriteLine(JsonSerializer.Serialize(new { runtime = Environment.Version.ToString(), os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, rows }, ReportJson));
            Assert(!Directory.EnumerateFileSystemEntries(Path.Combine(root, "results")).Any(), "No partial or committed files after scope disposal");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Callbacks : IHostCallbacks
    {
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken)
        {
            await Release.Task.WaitAsync(cancellationToken);
            return call.Payload;
        }
    }
}

internal sealed class PatternSource(long remaining) : Stream
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int count = (int)Math.Min(remaining, buffer.Length);
        buffer.Span[..count].Fill(0x5a);
        remaining -= count;
        return ValueTask.FromResult(count);
    }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class CheckingSink : Stream
{
    internal long Bytes;
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (buffer.Span.ContainsAnyExcept((byte)0x5a)) throw new InvalidDataException("Corrupt source block.");
        Bytes += buffer.Length;
        return ValueTask.CompletedTask;
    }
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
