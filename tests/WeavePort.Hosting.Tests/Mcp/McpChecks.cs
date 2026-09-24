using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;

internal static class McpChecks
{
    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new { });
    private static int _checks;
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "wp-mcp-" + Guid.NewGuid().ToString("N"));
    private static ProcessProfile Profile(ProcessProtocol protocol, string fault = "none", TimeSpan? timeout = null) => new(
        Environment.ProcessPath!, [typeof(McpChecks).Assembly.Location, "--mcp-worker", fault], true, Root, timeout: timeout ?? TimeSpan.FromSeconds(5))
    {
        Protocol = protocol
    };
    private static Task<IPluginSession> BindAsync(PluginHost host, ProcessProfile profile, string tenant = "A", string[]? grants = null) => host.BindAsync(
        new PluginContext(tenant, "fixture", "1", "test", JsonSerializer.SerializeToElement(new
        {
            secret = "not-for-mcp"
        })), profile, new NoCallbacks(), grants ?? []);
    private static Task<InvocationResult> CallAsync(IPluginSession session, string name, object? arguments = null, CancellationToken token = default) => session.InvokeAsync(
        "tools/call", JsonSerializer.SerializeToElement(new
        {
            name,
            arguments = arguments ?? new
            {
            }
        }), token);
    private static void Check(bool value, string name)
    {
        if (!value)
        {
            throw new Exception(name);
        }
        _checks++;
    }
    internal static async Task<int> RunAsync()
    {
        try
        {
            foreach (var protocol in new[] { ProcessProtocol.Mcp20251125, ProcessProtocol.Mcp20260728 })
            {
                await FunctionalAsync(protocol);
                await FaultsAsync(protocol);
                await LifecycleAsync(protocol);
                await RetentionAsync(protocol);
            }
            await ConfigAsync();
            await MixedAsync();
            using var output = new MemoryStream();
            try
            {
                await McpMessages.WriteAsync(output, "1", "tools/call", JsonSerializer.SerializeToElement(new
                {
                    name = "echo",
                    arguments = new
                    {
                        text = new string('x', Frames.MaximumBytes)
                    }
                }), "2026-07-28", default);
                throw new Exception("oversize MCP output accepted");
            }
            catch (InvalidDataException) { Check(output.Length == 0, "MCP oversize output rejected atomically"); }
            return _checks;
        }
        finally
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }
    }
    private static async Task FunctionalAsync(ProcessProtocol protocol)
    {
        await using var host = new PluginHost();
        await using var a = await BindAsync(host, Profile(protocol));
        var list = await a.InvokeAsync("tools/list", Empty);
        Check(list.Status == "ok" && list.Value.GetProperty("tools").GetArrayLength() == 2 && list.Value.GetProperty("nextCursor").GetString() == "next", "discovery and pagination");
        string instance = a.Instance;
        var echo = await CallAsync(a, "echo", new
        {
            text = "Grüße 🐝"
        });
        Check(echo.Status == "ok" && echo.Value.GetProperty("structuredContent").GetProperty("text").GetString() == "Grüße 🐝", "echo complete result");
        Check(!echo.Value.GetProperty("received").GetRawText().Contains("not-for-mcp"), "no implicit context secret");
        Check(a.Instance == instance, "multiple tools share process");
        var failure = await CallAsync(a, "fail");
        Check(failure.Status == "ok" && failure.Value.GetProperty("isError").GetBoolean(), "tool failure preserved");
        Check((await CallAsync(a, "echo")).Status == "ok" && a.Instance == instance, "tool failure retains worker");
        var denied = await a.InvokeAsync("resources/read", Empty);
        Check(denied.Status == "protocol-error", "unsupported method refused");
        var metadata = await a.InvokeAsync("tools/call", JsonSerializer.SerializeToElement(new
        {
            name = "echo",
            _meta = new
            {
                secret = "forged"
            }
        }));
        Check(metadata.Status == "protocol-error", "cannot inject protocol metadata");
    }
    private static async Task FaultsAsync(ProcessProtocol protocol)
    {
        foreach (string fault in new[] { "duplicate", "wrong-id", "both", "request", "depth", "oversize", "malformed", "utf8", "flood", "interaction", "interaction-content", "version", "eof" })
        {
            await using var host = new PluginHost();
            await using var a = await BindAsync(host, Profile(protocol, fault));
            var result = await CallAsync(a, "echo");
            Check(result.Status == (fault == "eof" ? "failed" : "protocol-error"), "fault " + fault + ": " + result.Status);
            Check(host.Snapshot.Workers == 0, "fault cleanup " + fault);
        }
        await using var pingHost = new PluginHost();
        await using var ping = await BindAsync(pingHost, Profile(protocol, "ping"));
        Check((await CallAsync(ping, "echo")).Status == (protocol == ProcessProtocol.Mcp20251125 ? "ok" : "protocol-error"), "legacy ping only; no modern server requests");
        await using var noisyHost = new PluginHost();
        await using var noisy = await BindAsync(noisyHost, Profile(protocol, "stderr"));
        Check((await CallAsync(noisy, "echo")).Status == "ok", "stderr drained without deadlock");
    }
    private static async Task LifecycleAsync(ProcessProtocol protocol)
    {
        await using var host = new PluginHost(options: new WorkerPoolOptions(MaximumWorkers: 3, MaximumPristineWorkers: 0, MaximumWorkersPerTenant: 1));
        await using var a = await BindAsync(host, Profile(protocol, timeout: TimeSpan.FromSeconds(1)));
        await using var b = await BindAsync(host, Profile(protocol), "B");
        await CallAsync(a, "counter");
        var first = await CallAsync(b, "counter");
        string original = b.Instance;
        await using var excess = await BindAsync(host, Profile(protocol));
        Check((await CallAsync(excess, "echo")).Status == "busy", "tenant admission while global capacity remains");
        await using var c = await BindAsync(host, Profile(protocol), "C");
        Check((await CallAsync(c, "echo")).Status == "ok", "another tenant uses remaining capacity");
        await using var d = await BindAsync(host, Profile(protocol), "D");
        Check((await CallAsync(d, "echo")).Status == "busy", "global admission");
        await c.DisposeAsync();
        await d.DisposeAsync();
        Check((await CallAsync(a, "crash")).Status == "failed", "crash surfaced");
        Check((await CallAsync(b, "counter")).Value.GetProperty("structuredContent").GetProperty("counter").GetInt32() == 2 && b.Instance == original, "peer state after crash");
        Check((await CallAsync(a, "counter")).Value.GetProperty("structuredContent").GetProperty("counter").GetInt32() == 1, "fresh replacement without replay");
        Check((await CallAsync(a, "hang")).Status == "timeout", "deadline");
        Check((await CallAsync(b, "echo")).Status == "ok" && b.Instance == original, "peer survives timeout");
        await CallAsync(a, "echo");
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var cancelled = await CallAsync(a, "hang", token: cancel.Token);
        Check(cancelled.Status == "cancelled" && cancelled.MayHaveExecuted, "cancellation uncertain outcome");
        await a.DisposeAsync();
        await b.DisposeAsync();
        await excess.DisposeAsync();
        Check(host.Snapshot.Workers == 0 && host.Snapshot.Bindings == 0, "all resources released");
    }
    private static async Task RetentionAsync(ProcessProtocol protocol)
    {
        await using var host = new PluginHost(options: new WorkerPoolOptions(MaximumWorkers: 4, MaximumPristineWorkers: 1));
        var profile = Profile(protocol) with
        {
            IdleTimeout = TimeSpan.FromMilliseconds(10)
        };
        await host.PrewarmAsync(profile, "1", 1);
        Check(host.Snapshot.Pristine == 1, "MCP prewarm ready without customer context");
        await using var session = await BindAsync(host, profile);
        Check((await CallAsync(session, "counter")).Value.GetProperty("structuredContent").GetProperty("counter").GetInt32() == 1, "pristine assigned once");
        string old = session.Instance;
        await Task.Delay(25);
        await host.MaintainAsync();
        var fresh = await CallAsync(session, "counter");
        Check(fresh.Status == "ok" && session.Instance != old && fresh.Value.GetProperty("structuredContent").GetProperty("counter").GetInt32() == 1, "idle replacement starts clean");
        await session.RestartAsync();
        Check((await CallAsync(session, "counter")).Value.GetProperty("structuredContent").GetProperty("counter").GetInt32() == 1, "explicit restart");
    }
    private static async Task MixedAsync()
    {
        await using var host = new PluginHost(options: new WorkerPoolOptions(MaximumWorkers: 2, MaximumPristineWorkers: 0));
        await using var mcp = await BindAsync(host, Profile(ProcessProtocol.Mcp20260728));
        var nativeProfile = new ProcessProfile(Environment.ProcessPath!, [typeof(McpChecks).Assembly.Location, "--lifecycle-worker"], true, Root);
        await using var native = await BindAsync(host, nativeProfile, "B");
        Check((await CallAsync(mcp, "echo")).Status == "ok" && (await native.InvokeAsync("echo", Empty)).Value.GetInt32() == 42, "mixed protocols in one host");
        Check(host.Snapshot.Workers == 2 && mcp.Instance != native.Instance, "mixed shared accounting, separate processes");
    }
    private static async Task ConfigAsync()
    {
        await using var host = new PluginHost();
        foreach (var profile in new[] { Profile((ProcessProtocol)55), Profile(ProcessProtocol.Mcp20260728) with { UseUnixSocket = true } })
        {
            try
            {
                await BindAsync(host, profile);
                throw new Exception("invalid config accepted");
            }
            catch (ArgumentException) { _checks++; }
        }
        try
        {
            await BindAsync(host, Profile(ProcessProtocol.Mcp20260728), grants: ["secret.read"]);
            throw new Exception("grants accepted");
        }
        catch (NotSupportedException) { _checks++; }
        Check(host.Snapshot.Bindings == 0 && host.Snapshot.Workers == 0, "invalid settings before registration");
    }
    internal static async Task InteropAsync(string executable, string server)
    {
        try
        {
            foreach (var protocol in new[] { ProcessProtocol.Mcp20251125, ProcessProtocol.Mcp20260728 })
            {
                await using var host = new PluginHost();
                await using var session = await BindAsync(host, new ProcessProfile(executable, [server], true, Root) { Protocol = protocol });
                var list = await session.InvokeAsync("tools/list", Empty);
                Check(list.Status == "ok" && list.Value.GetProperty("tools").GetArrayLength() >= 2, "official discovery " + protocol + ": " + list.Status);
                var result = await CallAsync(session, "normalize", new
                {
                    text = " hello   world "
                });
                Check(result.Status == "ok" && result.Value.GetProperty("structuredContent").GetProperty("text").GetString() == "hello world", "official tool " + protocol + ": " + result.Status);
                Console.WriteLine("PASS official MCP SDK interoperability: " + protocol);
            }
        }
        finally
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }
    }
    private sealed class NoCallbacks : IHostCallbacks
    {
        public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken) => throw new Exception("MCP must never invoke callbacks");
    }
}
