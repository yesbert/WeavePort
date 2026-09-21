using System.Diagnostics;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;

string root = Path.GetFullPath(args.ElementAtOrDefault(0) ?? Environment.CurrentDirectory);
string python = args.ElementAtOrDefault(1) ?? FindExecutable("python3");
string node = args.ElementAtOrDefault(2) ?? FindExecutable("node");
string csharp = Environment.GetEnvironmentVariable("WP_SHARED_CSHARP_FIXTURE") ?? Path.Combine(root, "tests/WeavePort.ConcurrentSdkTests/bin/Release/net10.0/WeavePort.ConcurrentSdkTests.dll");
foreach (var (language, executable, fixture) in new[] { ("python", python, "shared.py"), ("typescript", node, "shared.mjs"), ("csharp", FindExecutable("dotnet"), csharp) })
{
    Console.WriteLine($"Shared host qualification: {language}");
    List<string> launch = language == "csharp" ? [fixture, "--host-shared"] : [Path.Combine(root, "tests/WeavePort.Shared.Tests/fixtures", fixture)];
    string? sdkPath = language == "csharp" ? null : Environment.GetEnvironmentVariable(language == "python" ? "WP_SHARED_PYTHON_SDK" : "WP_SHARED_NODE_SDK");
    if (sdkPath is not null) launch.AddRange(["--sdk-path", sdkPath]);
    var profile = new ProcessProfile(executable, launch, trustedCode: true, reservedMemoryMiB: 128, timeout: TimeSpan.FromSeconds(5)) { ReusePolicy = WorkerReusePolicy.Shared, MaximumCallbacks = 3 };
    await RunCaseAsync(language, "Overlap", () => OverlapAsync(profile));
    await RunCaseAsync(language, "CallbacksAndErrors", () => CallbacksAndErrorsAsync(profile));
    await RunCaseAsync(language, "Cancellation", () => CancellationAsync(profile));
    await RunCaseAsync(language, "CrashRecovery", () => CrashRecoveryAsync(profile));
    await RunCaseAsync(language, "OwnershipAndCoexistence", () => OwnershipAndCoexistenceAsync(profile));
    await RunCaseAsync(language, "Shutdown", () => ShutdownAsync(profile));
    await RunCaseAsync(language, "LargerCallbackBudget", () => LargerCallbackBudgetAsync(profile));
    await RunCaseAsync(language, "CancellationGrace", () => CancellationGraceAsync(profile));
    await RunCaseAsync(language, "CleanupFailure", () => CleanupFailureAsync(profile));
    await RunCaseAsync(language, "Silence", () => SilenceAsync(profile));
    await RunCaseAsync(language, "DetachedCallbackCapacity", () => DetachedCallbackCapacityAsync(profile));
    if (language != "csharp") await RunCaseAsync(language, "Malformed", () => MalformedAsync(profile));
}
await BlockedWriterAsync(root, python);
await CatalogChecks.RunAsync(root, python, node);
await BoundedStateChecks.RunAsync(root, python);
Console.WriteLine("PASS: real-host shared execution, callback identity, cancellation, failure, restart, ownership and shutdown.");

static async Task RunCaseAsync(string language, string name, Func<Task> execute)
{
    Console.WriteLine($"BEGIN {language}: {name}");
    await execute();
    Console.WriteLine($"END {language}: {name}");
}

static string FindExecutable(string name) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(directory => Path.Combine(directory, name)).First(File.Exists);
static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
static PluginContext Context() => new("owner", "shared-fixture", "1", "test", Json(new { }));
static PluginHost Host(bool failFast = false) => new(new SchedulingOptions { MaximumWorkers = 3, MaximumHeavyCalls = 1, MaximumPristineWorkers = 0, MemoryBudgetMiB = 512, MaximumCallsPerTenant = 16, QueueTimeout = failFast ? TimeSpan.Zero : TimeSpan.FromSeconds(3) });
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static async Task WaitAsync(Func<bool> condition, string message)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    while (!condition()) { await Task.Delay(10, timeout.Token); }
    Check(condition(), message);
}
static async Task<string> OutcomeAsync(Task<JsonElement> call)
{
    try { await call; return "ok"; }
    catch (PluginCallException error) { return error.Status; }
    catch (OperationCanceledException) { return "cancelled"; }
}

static async Task OverlapAsync(ProcessProfile profile)
{
    await using var host = Host();
    var callbacks = new Callbacks(16);
    await using var shared = await host.ShareAsync(Context(), profile, new() { Degree = 16 }, callbacks, ["barrier"]);
    var clients = Enumerable.Range(0, 16).Select(i => shared.For($"tenant-{i}")).ToArray();
    var calls = clients.Select((client, i) => client.CallAsync("barrier", Json(new { index = i }))).ToArray();
    JsonElement[] results;
    try { results = await Task.WhenAll(calls).WaitAsync(TimeSpan.FromSeconds(8)); }
    catch { Console.WriteLine(Json(new { shared = shared.Snapshot, scheduling = host.Scheduling, callbacks.MaximumActive, statuses = calls.Select(c => c.Status.ToString()) })); throw; }
    for (int i = 0; i < results.Length; i++)
    {
        Check(results[i].GetProperty("tenant").GetString() == $"tenant-{i}", "Invocation tenant crossed.");
        Check(results[i].GetProperty("callback").GetProperty("tenant").GetString() == $"tenant-{i}", "Callback tenant crossed.");
    }
    Check(callbacks.MaximumActive == 16, "All sixteen handlers must overlap at a functional barrier.");
    Check(shared.Snapshot.ReadyWorkers == 1 && shared.Snapshot.ActiveCalls == 0, "Single resident worker accounting.");
    foreach (var client in clients) await client.DisposeAsync();
}

static async Task CallbacksAndErrorsAsync(ProcessProfile profile)
{
    await using var host = Host();
    var callbacks = new Callbacks();
    await using var shared = await host.ShareAsync(Context(), profile, new() { Degree = 4 }, callbacks, ["echo", "throw"]);
    await using var a = shared.For("A");
    await using var b = shared.For("B");
    string instance = (await a.CallAsync("echo", Json(new { }))).GetProperty("pid").ToString();
    Check(await OutcomeAsync(a.CallAsync("callbacks", Json(new { count = 4 }))) == "failed", "Per-invocation callback limit.");
    Check(await OutcomeAsync(a.CallAsync("callbacks", Json(new { operation = "denied" }))) == "failed", "Denied callback must fail only its invocation.");
    Check(await OutcomeAsync(a.CallAsync("callbacks", Json(new { operation = "throw" }))) == "failed", "Host callback exception must fail only its invocation.");
    Check(await OutcomeAsync(a.CallAsync("fail", Json(new { }))) == "failed", "Author failure must surface.");
    JsonElement result = await b.CallAsync("callbacks", Json(new { count = 3 }));
    Check(result.GetProperty("callback").GetProperty("tenant").GetString() == "B", "Callback budget reset per invocation.");
    Check((await b.CallAsync("echo", Json(new { }))).GetProperty("pid").ToString() == instance, "Ordinary failures retired healthy worker.");
    Check(shared.Snapshot.Restarts == 0, "Isolated errors unexpectedly restarted process.");
}

static async Task CancellationAsync(ProcessProfile profile)
{
    await using var host = Host(true);
    var callbacks = new Callbacks();
    await using var shared = await host.ShareAsync(Context(), profile, new() { Degree = 1, CancellationGrace = TimeSpan.FromSeconds(2) }, callbacks, ["entered"]);
    await using var a = shared.For("A");
    await using var b = shared.For("B");
    using var stop = new CancellationTokenSource();
    Task<JsonElement> pending = a.CallAsync("delay", Json(new { milliseconds = 350, announce = true }), stop.Token);
    await callbacks.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
    stop.Cancel();
    Check(await OutcomeAsync(pending) == "cancelled", "Caller cancellation must return promptly.");
    Check(shared.Snapshot.ActiveCalls == 1 && shared.Snapshot.AbandonedCalls == 1, "Cancelled work released capacity before completion.");
    Check(await OutcomeAsync(b.CallAsync("echo", Json(new { }))) == "busy", "Retained slot admitted extra work.");
    await WaitAsync(() => shared.Snapshot.ActiveCalls == 0, "Cancelled slot did not clear after terminal.");
    Check(await OutcomeAsync(b.CallAsync("echo", Json(new { }))) == "ok", "Cancelled work poisoned worker.");
}

static async Task CrashRecoveryAsync(ProcessProfile profile)
{
    await using var host = Host();
    await using var shared = await host.ShareAsync(Context(), profile, new() { Degree = 4, MaximumRestarts = 1 }, new Callbacks(), []);
    await using var a = shared.For("A");
    await using var b = shared.For("B");
    Task<JsonElement> pending = a.CallAsync("delay", Json(new { milliseconds = 2000 }));
    Task<JsonElement> crash = b.CallAsync("crash", Json(new { milliseconds = 100 }));
    Check(await OutcomeAsync(crash) == "failed" && await OutcomeAsync(pending) == "failed", "Crash must fail all in-flight calls without replay.");
    await WaitAsync(() => shared.Snapshot.ReadyWorkers == 1 && shared.Snapshot.Restarts == 1, "Worker did not restart.");
    Check(await OutcomeAsync(a.CallAsync("echo", Json(new { }))) == "ok", "Restart did not restore residency.");
    Check(await OutcomeAsync(a.CallAsync("crash", Json(new { }))) == "failed", "Second crash must fail invocation.");
    await WaitAsync(() => shared.Snapshot.Disabled, "Restart budget was not enforced.");
    Check(shared.Snapshot.ReadyWorkers == 0, "Disabled worker appears ready.");
}

static async Task OwnershipAndCoexistenceAsync(ProcessProfile profile)
{
    await using var host = Host();
    await using var shared = await host.ShareAsync(Context(), profile, new() { Degree = 2 }, new Callbacks(), []);
    await using var client = shared.For("A");
    bool rejected = false;
    try { await foreach (var _ in client.StreamAsync("items", Json(new { }))) { } }
    catch (NotSupportedException) { rejected = true; }
    Check(rejected, "Shared streaming must be rejected before dispatch.");
    rejected = false;
    try { await host.BindAsync(Context(), profile, new Callbacks(), []); }
    catch (NotSupportedException) { rejected = true; }
    Check(rejected, "Bind must reject shared ownership.");
    var serial = new ProcessProfile(profile.Executable, profile.Arguments.Append("--exclusive"), trustedCode: true, reservedMemoryMiB: 128);
    await using var binding = await host.BindAsync(Context(), serial, new Callbacks(), []);
    await using var exclusive = new LocalPluginClient(binding);
    Check(await OutcomeAsync(exclusive.CallAsync("echo", Json(new { }))) == "ok", "Exclusive binding failed alongside shared residency.");
    Check(await OutcomeAsync(client.CallAsync("echo", Json(new { }))) == "ok", "Exclusive binding damaged shared residency.");
    Check(host.Snapshot.Workers == 2, "Exclusive/shared worker budget is not unified.");
}

static async Task ShutdownAsync(ProcessProfile profile)
{
    var host = Host();
    var callbacks = new Callbacks();
    var shared = await host.ShareAsync(Context(), profile, new() { Degree = 2 }, callbacks, ["entered"]);
    await using var client = shared.For("shutdown");
    Task<JsonElement> pending = client.CallAsync("delay", Json(new { milliseconds = 100, announce = true }));
    await callbacks.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
    await host.DisposeAsync();
    Check(await OutcomeAsync(pending) is "ok" or "cancelled" or "failed", "Shutdown did not complete pending operation.");
    Check(host.Snapshot.Workers == 0, "Shutdown leaked process reservations.");
    await host.DisposeAsync();
    await shared.DisposeAsync();
}

static async Task LargerCallbackBudgetAsync(ProcessProfile profile)
{
    await using var host = Host();
    await using var shared = await host.ShareAsync(Context(), profile with { MaximumCallbacks = 64 }, new() { Degree = 2 }, new Callbacks(), ["echo"]);
    await using var client = shared.For("A");
    Check(await OutcomeAsync(client.CallAsync("callbacks", Json(new { count = 40 }))) == "ok", "Operator-approved callback budget of 64 must support 40 callbacks.");
    await using var defaults = await host.ShareAsync(Context(), profile with { MaximumCallbacks = 8 }, new() { Degree = 2 }, new Callbacks(), ["echo"]);
    await using var limited = defaults.For("B");
    Check(await OutcomeAsync(limited.CallAsync("callbacks", Json(new { count = 9 }))) == "failed", "Default callback budget must reject callback nine.");
}

static async Task CancellationGraceAsync(ProcessProfile profile)
{
    await using var host = Host();
    var callbacks = new Callbacks();
    await using var shared = await host.ShareAsync(Context(), profile, new() { Degree = 2, CancellationGrace = TimeSpan.FromMilliseconds(50) }, callbacks, ["entered"]);
    await using var a = shared.For("A");
    await using var b = shared.For("B");
    using var stop = new CancellationTokenSource();
    Task<JsonElement> pending = a.CallAsync("delay", Json(new { milliseconds = 2000, announce = true }), stop.Token);
    Task<JsonElement> other = b.CallAsync("delay", Json(new { milliseconds = 2000 }));
    await callbacks.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
    stop.Cancel();
    Check(await OutcomeAsync(pending) == "cancelled", "Abandoned caller did not return cancellation.");
    Check(await OutcomeAsync(other) == "failed", "Cancellation grace did not retire stuck worker.");
    await WaitAsync(() => shared.Snapshot.ReadyWorkers == 1 && shared.Snapshot.Restarts == 1, "Cancellation retirement did not replace worker.");
}

static async Task CleanupFailureAsync(ProcessProfile profile)
{
    await using var host = Host();
    await using var shared = await host.ShareAsync(Context(), profile, new() { Degree = 2 }, new Callbacks(), []);
    await using var a = shared.For("A");
    await using var b = shared.For("B");
    Task<JsonElement> pending = a.CallAsync("delay", Json(new { milliseconds = 2000 }));
    await WaitAsync(() => shared.Snapshot.ActiveCalls == 1, "Peer call not admitted.");
    Check(await OutcomeAsync(b.CallAsync("cleanupFail", Json(new { }))) == "failed", "Cleanup failure must fail the call.");
    Check(await OutcomeAsync(pending) == "failed", "Cleanup failure did not retire entire channel.");
    await WaitAsync(() => shared.Snapshot.ReadyWorkers == 1 && shared.Snapshot.Restarts == 1, "Cleanup-failed worker did not restart.");
}

static async Task SilenceAsync(ProcessProfile profile)
{
    await using var host = Host();
    await using var shared = await host.ShareAsync(Context(), profile, new() { Degree = 1, SilenceTimeout = TimeSpan.FromMilliseconds(100) }, new Callbacks(), []);
    await using var client = shared.For("A");
    Check(await OutcomeAsync(client.CallAsync("delay", Json(new { milliseconds = 2000 }))) == "failed", "Bounded silence must retire an unresponsive channel.");
    await WaitAsync(() => shared.Snapshot.ReadyWorkers == 1 && shared.Snapshot.Restarts == 1, "Silent channel did not restart.");
}

static async Task MalformedAsync(ProcessProfile profile)
{
    await using var host = Host();
    await using var shared = await host.ShareAsync(Context(), profile, new() { Degree = 1 }, new Callbacks(), []);
    await using var client = shared.For("A");
    for (int i = 0; i < 2; i++)
    {
        Check(await OutcomeAsync(client.CallAsync("malformed", Json(new { invalidJson = i == 0 }))) == "failed", "Malformed JSON or unknown identity must fail the channel.");
        int restarts = i + 1;
        await WaitAsync(() => shared.Snapshot.ReadyWorkers == 1 && shared.Snapshot.Restarts == restarts, "Malformed channel did not restart.");
    }
}

static async Task BlockedWriterAsync(string root, string python)
{
    await using var host = Host();
    var profile = new ProcessProfile(python, [Path.Combine(root, "tests/WeavePort.Shared.Tests/fixtures/blocked.py")], trustedCode: true, reservedMemoryMiB: 128, timeout: TimeSpan.FromSeconds(30)) { ReusePolicy = WorkerReusePolicy.Shared };
    await using var shared = await host.ShareAsync(Context(), profile, new() { Degree = 1, CancellationGrace = TimeSpan.FromMilliseconds(200), SilenceTimeout = TimeSpan.FromMinutes(1), MaximumRestarts = 0 }, new Callbacks(), []);
    await using var client = shared.For("A");
    using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
    var watch = Stopwatch.StartNew();
    string outcome = await OutcomeAsync(client.CallAsync("echo", Json(new { data = new string('x', 500 * 1024) }), stop.Token)).WaitAsync(TimeSpan.FromSeconds(3));
    Check(outcome == "cancelled" && watch.Elapsed < TimeSpan.FromSeconds(2), "Blocked pipe write prevented prompt cancellation.");
    await WaitAsync(() => shared.Snapshot.Disabled, "Partially written channel was not retired.");
}

static async Task DetachedCallbackCapacityAsync(ProcessProfile profile)
{
    await using var host = new PluginHost(new SchedulingOptions { MaximumCallsPerTenant = 1, MaximumWorkers = 2, MaximumHeavyCalls = 0, MaximumPristineWorkers = 0, MemoryBudgetMiB = 512 });
    var callbacks = new RetainedCallbacks();
    await using var shared = await host.ShareAsync(Context(), profile, new() { Degree = 2 }, callbacks, ["block", "echo"]);
    await using var a = shared.For("A");
    await using var b = shared.For("B");
    using var stop = new CancellationTokenSource();
    Task<JsonElement> pending = a.CallAsync("callbacks", Json(new { operation = "block" }), stop.Token);
    await callbacks.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
    stop.Cancel();
    Check(await OutcomeAsync(pending) == "cancelled", "Blocked callback did not cancel caller.");
    await WaitAsync(() => shared.Snapshot.ActiveCalls == 0, "Cancelled callback did not terminate SDK invocation.");
    try
    {
        Check(await OutcomeAsync(a.CallAsync("callbacks", Json(new { }))) == "failed", "Detached callback released tenant callback capacity early.");
        Check(await OutcomeAsync(b.CallAsync("callbacks", Json(new { }))) == "ok", "Detached tenant callback blocked another tenant.");
    }
    finally { callbacks.Release.TrySetResult(); }
}

sealed class RetainedCallbacks : IHostCallbacks
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken token)
    {
        if (call.Operation == "block") { Entered.TrySetResult(); await Release.Task; }
        return JsonSerializer.SerializeToElement(new { tenant = call.Context.Tenant });
    }
}

sealed class Callbacks(int barrierSize = 1) : IHostCallbacks
{
    private readonly TaskCompletionSource _barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _active;
    private int _entered;
    public int MaximumActive { get; private set; }
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken token)
    {
        if (call.Operation == "throw") throw new InvalidOperationException("Deliberate callback failure.");
        if (call.Operation == "entered") Entered.TrySetResult();
        if (call.Operation == "barrier")
        {
            int active = Interlocked.Increment(ref _active);
            lock (_barrier) MaximumActive = Math.Max(MaximumActive, active);
            if (Interlocked.Increment(ref _entered) == barrierSize) _barrier.TrySetResult();
            await _barrier.Task.WaitAsync(token);
            Interlocked.Decrement(ref _active);
        }
        return JsonSerializer.SerializeToElement(new { tenant = call.Context.Tenant, value = call.Payload });
    }
}
