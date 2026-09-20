using System.Reflection;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;

if (args.Length > 1 && args[0] == "--raw") { RawFixture.Run(args[1]); return; }
if (args.Contains("--worker")) { await WorkerFixture.RunAsync(); return; }
string root = args[0];
string python = args[1];
string node = args[2];
string pythonSdk = args.Length > 3 ? args[3] : Path.Combine(root, "sdks/python");
string tsSdk = args.Length > 4 ? args[4] : Path.Combine(root, "sdks/typescript/dist/index.js");
string fixture = Path.Combine(root, "tests/WeavePort.ReuseTests");
int checks = 0;
var callbacks = new Callbacks();
var profiles = new Dictionary<string, ExecutionProfile>
{
    ["csharp"] = new ProcessProfile(Environment.ProcessPath!, [Assembly.GetExecutingAssembly().Location, "--worker"], true, reservedMemoryMiB:64),
    ["python"] = new ProcessProfile(python, [Path.Combine(fixture, "worker.py"), pythonSdk], true, reservedMemoryMiB:64),
    ["typescript"] = new ProcessProfile(node, [Path.Combine(fixture, "worker.mjs"), tsSdk], true, reservedMemoryMiB:64)
};
if (args.Length > 5)
{
    profiles = new() { ["docker-" + args[5]] = new DockerProfile(args[6], MemoryMiB:64, CpuCount:1) };
}
foreach (var (language, original) in profiles)
{
    foreach (var policy in new[] { WorkerReusePolicy.CustomerBound, WorkerReusePolicy.ApprovedSessions })
    {
        await using var host = new PluginHost(options: new WorkerPoolOptions(MaximumWorkers:2, MemoryBudgetMiB:128, MaximumPristineWorkers:0));
        var profile = original with { ReusePolicy = policy, Timeout = TimeSpan.FromSeconds(3) };
        await using var a = await host.BindAsync(Context("a"), profile, callbacks, ["who"]);
        await using var b = await host.BindAsync(Context("b"), profile, callbacks, ["who"]);
        var first = await Call(a, "session"); CheckSession(first, "a");
        var second = await Call(b, "session"); CheckSession(second, "b");
        Assert((first.Instance == second.Instance) == (policy == WorkerReusePolicy.ApprovedSessions), language + " policy boundary");
        for (int i = 0; i < 10; i++) CheckSession(await Call(i % 2 == 0 ? a : b, "session"), i % 2 == 0 ? "a" : "b");
        Assert(host.Snapshot.Workers == (policy == WorkerReusePolicy.ApprovedSessions ? 1 : 2), "pool size");
        await Call(a, "stash");
        var probe = await Call(b, "probe");
        Assert((probe.Value.GetProperty("hidden").ValueKind == JsonValueKind.String) == (policy == WorkerReusePolicy.ApprovedSessions), "accepted hidden-global risk");
        var stream = await a.InvokeAsync("$sdk.start", JsonSerializer.SerializeToElement(new { operation = "rows", input = new {} }));
        Assert(stream.Status == "ok", "stream started");
        var during = await Call(b, "session"); CheckSession(during, "b");
        Assert(stream.Instance != during.Instance, "active stream cannot be transferred");
        var batch = await a.InvokeAsync("$sdk.next", stream.Value);
        Assert(batch.Status == "ok" && batch.Value.GetProperty("items")[0].GetProperty("tenant").GetString() == "a", "stream context preserved");
        var closed = await a.InvokeAsync("$sdk.close", stream.Value);
        Assert(closed.Status == "ok", "stream closed");
        CheckSession(await Call(a, "session"), "a");
        if (policy == WorkerReusePolicy.ApprovedSessions)
        {
            Assert(host.Snapshot.ReuseHits > 0 && host.Snapshot.SessionReturns > 0, "reuse observable");
            foreach (string fault in new[] { "cleanup-fail", "cleanup-hang" })
            {
                var failed = await Call(a, fault);
                Assert(failed.Status == (fault == "cleanup-hang" ? "timeout" : "failed"), "cleanup failure surfaced");
                var recovered = await Call(b, "session"); CheckSession(recovered, "b");
                Assert(failed.Instance != recovered.Instance, "failed cleanup retires worker");
            }
            Assert(host.Snapshot.SessionCleanupFailures > 0, "cleanup failure accounting");
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            var cancelled = await a.InvokeAsync("$sdk.call", JsonSerializer.SerializeToElement(new { operation = "delay", input = new { ms = 5000 } }), cancel.Token);
            Assert(cancelled.Status == "cancelled" && cancelled.MayHaveExecuted, "explicit cancellation after dispatch");
            var afterCancel = await Call(b, "session"); CheckSession(afterCancel, "b");
            Assert(afterCancel.Instance != cancelled.Instance, "cancelled worker cannot be reused");
        }
        Console.WriteLine($"{language} {policy}: passed");
    }
    await Scheduler(original);
    await Lifecycle(original);
}
if (args.Length <= 5)
{
    foreach (string mode in new[] { "legacy", "missing", "invalid", "duplicate" })
    {
        await using var host = new PluginHost();
        var profile = new ProcessProfile(Environment.ProcessPath!, [Assembly.GetExecutingAssembly().Location,"--raw",mode],true)
            { ReusePolicy=WorkerReusePolicy.ApprovedSessions };
        await using var binding = await host.BindAsync(Context("raw"),profile,callbacks,[]);
        var result = await Call(binding,"session");
        Assert(result.Status=="protocol-error" && host.Snapshot.Workers==0,"missing/malformed cleanup protocol fails closed: "+mode);
    }
}
Console.WriteLine($"PASS {checks} reuse assertions");

async Task Scheduler(ExecutionProfile original)
{
    await using var host = new ScheduledPluginHost(new SchedulingOptions { MemoryBudgetMiB=128, MaximumWorkers=2, MaximumPristineWorkers=0, MaximumHeavyCalls=0 });
    var profile = original with { ReusePolicy=WorkerReusePolicy.ApprovedSessions };
    var plugins = new List<ScheduledPlugin>();
    try
    {
        for (int i=0;i<20;i++) plugins.Add(await host.RegisterAsync(Context("scheduled-"+i), profile, callbacks,["who"]));
        var ids = new HashSet<string>();
        foreach(var plugin in plugins)
        {
            var result = await plugin.InvokeAsync("$sdk.call",JsonSerializer.SerializeToElement(new{operation="session", input=new{}}));
            CheckSession(result,plugin.Tenant);
            Assert(host.Snapshot.Active == 0, "awaited completion releases scheduler activity before handoff");
            ids.Add(result.Instance);
        }
        Assert(ids.Count==1 && host.Snapshot.Runtime.ReuseHits==19,"scheduler shares clean worker across many registrations");
        var concurrent = await Task.WhenAll(plugins.Select(plugin => plugin.InvokeAsync("$sdk.call",JsonSerializer.SerializeToElement(new{operation="session",input=new{}}))));
        for(int i=0;i<concurrent.Length;i++) CheckSession(concurrent[i],plugins[i].Tenant);
        Assert(host.Snapshot.Runtime.Workers<=2 && host.Snapshot.Failure is null,"concurrent scheduler respects global pool budget");
    }
    finally { foreach(var plugin in plugins) await plugin.DisposeAsync(); }
}

async Task Lifecycle(ExecutionProfile original)
{
    var profile = original with { ReusePolicy=WorkerReusePolicy.ApprovedSessions };
    var clock = new TestClock();
    await using var host = new PluginHost(options:new WorkerPoolOptions(MaximumWorkers:1,MemoryBudgetMiB:64,MaximumPristineWorkers:0)
        { ReusableIdleTimeout=TimeSpan.FromMinutes(1) }, timeProvider:clock);
    await using var a = await host.BindAsync(Context("a"),profile,callbacks,["who"]);
    var first = await Call(a,"session");CheckSession(first,"a");
    await a.DisposeAsync();
    await using var b = await host.BindAsync(Context("b") with { Plugin="other-plugin" },profile,callbacks,["who"]);
    var next = await Call(b,"session");CheckSession(next,"b");
    Assert(first.Instance==next.Instance,"disposing old binding does not destroy returned worker; compatible plugin switch");
    await b.RestartAsync();
    var restarted = await Call(b,"session"); CheckSession(restarted,"b");
    Assert(restarted.Instance != next.Instance,"explicit restart bypasses previously used clean workers");
    clock.Advance(TimeSpan.FromMinutes(2));
    await host.MaintainAsync();
    Assert(host.Snapshot.Workers==0,"clean worker idle expiry");
    await Call(b,"stash");
    string approvedInstance = b.Instance;
    await using var bound = await host.BindAsync(Context("bound"),original,callbacks,["who"]);
    var probe = await Call(bound,"probe");
    Assert(probe.Status=="ok" && probe.Instance!=approvedInstance && probe.Value.GetProperty("hidden").ValueKind==JsonValueKind.Null,"approved worker cannot enter customer-bound pool");
}

void CheckSession(InvocationResult result,string tenant)
{
    Assert(result.Status=="ok", "successful session: "+result.Status);
    Assert(result.Value.GetProperty("tenant").GetString()==tenant && result.Value.GetProperty("secret").GetString()=="secret-"+tenant,"current customer context");
    Assert(result.Value.GetProperty("expired").GetBoolean(),"old context expired");
    Assert(result.Value.GetProperty("callback").GetString()==tenant,"host checks current binding authority");
}
void Assert(bool condition,string name) { if(!condition) throw new InvalidDataException(name); checks++; }
static PluginContext Context(string tenant)=>new(tenant,"plugin","1","default",JsonSerializer.SerializeToElement(new{secret="secret-"+tenant}));
static Task<InvocationResult> Call(IPluginSession session,string operation)=>session.InvokeAsync("$sdk.call",JsonSerializer.SerializeToElement(new{operation,input=new{}}));
sealed class Callbacks:IHostCallbacks
{
    public ValueTask<JsonElement> InvokeAsync(HostCall call,CancellationToken token)=>ValueTask.FromResult(JsonSerializer.SerializeToElement(call.Context.Tenant));
}

sealed class TestClock:TimeProvider
{
    private long _now;
    public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
    public override long GetTimestamp()=>_now;
    public void Advance(TimeSpan duration)=>_now+=duration.Ticks;
}
