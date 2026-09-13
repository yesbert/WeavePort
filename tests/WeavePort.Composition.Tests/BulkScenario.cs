using System.Diagnostics;
using System.Text;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Composition;
using WeavePort.LocalTools;

internal sealed class BulkScenario : IAsyncDisposable
{
    private readonly PluginHost _host;
    private readonly bool _ownsHost;
    private readonly IPluginSession[] _sessions;
    private readonly string _root;
    private BulkScenario(string root, PluginHost? host) { _root = root; _sessions = new IPluginSession[3]; _ownsHost = host is null; _host = host ?? new PluginHost(options: new WorkerPoolOptions(MaximumWorkers: 256, MemoryBudgetMiB: 65536, MaximumConcurrentStarts: 16)); }
    internal static async Task<BulkScenario> CreateAsync(string configPath, string root, string tenant, PluginHost? host = null, CancellationToken token = default)
    {
        LocalConfiguration config = LocalConfiguration.Load(configPath);
        var scenario = new BulkScenario(root, host);
        try
        {
            string[] languages = ["csharp", "python", "typescript"];
            for (int i = 0; i < 3; i++)
            {
                scenario._sessions[i] = await scenario._host.BindAsync(new PluginContext(tenant, languages[i], "1", "bulk", JsonSerializer.SerializeToElement(new { })), config.Profile(languages[i], TimeSpan.FromSeconds(30)), new NoCallbacks(), [], token);
                if (scenario._sessions[i].Tenant != tenant) throw new InvalidOperationException("Host binding identity mismatch.");
                InvocationResult warm = await scenario._sessions[i].InvokeAsync("echo", JsonSerializer.SerializeToElement(new { }), token);
                if (warm.Status != "ok") throw new IOException("Warmup failed.");
            }
            scenario.Tenant = tenant;
            return scenario;
        }
        catch { await scenario.DisposeAsync(); throw; }
    }
    internal (long Rss, double Cpu) WorkerResources()
    {
        long rss = 0; double cpu = 0;
        foreach (IPluginSession session in _sessions)
        {
            int pid = int.Parse(session.Instance[(session.Instance.LastIndexOf("-p", StringComparison.Ordinal) + 2)..]);
            using var process = Process.GetProcessById(pid); rss += process.WorkingSet64; cpu += process.TotalProcessorTime.TotalSeconds;
        }
        return (rss, cpu);
    }
    internal Task<WorkloadResult> RunDocumentsAsync(WorkloadOptions options, Stream? export, CancellationToken token) =>
        DocumentWorkload.RunAsync(_root, Tenant, _sessions, options, export, token);
    internal string Tenant { get; private set; } = "";
    internal static byte[] Pattern(bool transformed) => Encoding.UTF8.GetBytes("{\"id\":1,\"text\":\"" + new string(transformed ? 'A' : 'a', 45) + "\"}\n");
    internal static Task<ResultHandle> GenerateAsync(ResultScope scope, int bytes, CancellationToken token = default) => scope.CreateAsync(async (stream, ct) =>
    {
        byte[] pattern = Pattern(false);
        if (pattern.Length != 64 || bytes % 64 != 0) throw new InvalidOperationException("Fixture alignment.");
        byte[] block = new byte[65536];
        for (int i = 0; i < block.Length; i++) block[i] = pattern[i % pattern.Length];
        for (int written = 0; written < bytes; written += block.Length) await stream.WriteAsync(block.AsMemory(0, Math.Min(block.Length, bytes - written)), ct);
    }, token);
    internal async Task<long> RunAsync(int bytes, bool fanOut, int chunkBytes, bool buffered = false, CancellationToken token = default, Stream? destination = null)
    {
        await using var scope = new ResultScope(_root, Tenant, new ResultLimits(512L << 20, 2L << 30, 16));
        ResultHandle result = await GenerateAsync(scope, bytes, token);
        if (fanOut)
        {
            var branches = _sessions.Select<IPluginSession, Func<ResultScope, ResultHandle, CancellationToken, Task<ResultHandle>>>(session =>
                (owner, input, ct) => Composition.MapAsync(owner, input, session, chunkBytes, ct)).ToArray();
            ResultHandle[] outputs = await Composition.FanOutAsync(scope, result, branches, 3, token);
            result = await Composition.ConcatenateAsync(scope, outputs, token);
        }
        else foreach (IPluginSession session in _sessions) result = await Composition.MapAsync(scope, result, session, chunkBytes, token);
        using var caller = new CheckingSink(Pattern(true));
        if (destination is not null) await scope.CopyToAsync(result, destination, token);
        else if (buffered)
        {
            using var buffer = new MemoryStream();
            await scope.CopyToAsync(result, buffer, token);
            buffer.Position = 0;
            await buffer.CopyToAsync(caller, token);
        }
        else await scope.CopyToAsync(result, caller, token);
        long expected = (long)bytes * (fanOut ? 3 : 1);
        if ((destination is null && caller.Count != expected) || result.Length != expected) throw new InvalidDataException("Caller result length mismatch.");
        return expected;
    }
    public async ValueTask DisposeAsync()
    {
        if (_ownsHost) await _host.DisposeAsync();
        else foreach (IPluginSession? session in _sessions) if (session is not null) await session.DisposeAsync();
    }
    private sealed class NoCallbacks : IHostCallbacks
    {
        public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken) => throw new UnauthorizedAccessException();
    }
}

internal sealed class CheckingSink(byte[] pattern) : Stream
{
    internal long Count { get; private set; }
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        foreach (byte item in buffer) { if (item != pattern[Count % pattern.Length]) throw new InvalidDataException("Caller content mismatch."); Count++; }
    }
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); Write(buffer.Span); return ValueTask.CompletedTask; }
    public override bool CanRead => false; public override bool CanWrite => true; public override bool CanSeek => false;
    public override long Length => Count; public override long Position { get => Count; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
