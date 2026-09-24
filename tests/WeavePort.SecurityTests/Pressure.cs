using System.Diagnostics;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;

internal static partial class Pressure
{
    internal static async Task RunAsync(Evidence evidence, int repeat)
    {
        await using var host = new PluginHost(options: new WorkerPoolOptions(MaximumWorkers: 2, MemoryBudgetMiB: 512, MaximumPristineWorkers: 0));
        var callbacks = new BoundaryCallbacks();
        await using IPluginSession victimSession = await BindAsync(host, callbacks, "B");
        await ValueAsync(victimSession, "workspace", new
        {
            text = "B-synthetic-private"
        });
        string victim = victimSession.Instance;
        int priorFailures = evidence.Failures;
        await SecurityObservations.RunAsync(evidence, "extended-" + repeat, victim);
        Assert(evidence.Failures == priorFailures, "Effective security preflight failed; pressure tests stopped");
        JsonElement victimInspect = await Policy.InspectAsync(victim);
        Policy.Validate(victimInspect);
        await evidence.CheckAsync("r" + repeat + ": 16 unsafe policy negative controls", () =>
        {
            Assert(Policy.NegativeControls(victimInspect) == 16, "Negative controls missing");
            return Task.CompletedTask;
        });
        await new Scenario(evidence, repeat, host, callbacks, victimSession, victimInspect).RunAsync();
    }
    private static Task<IPluginSession> BindAsync(PluginHost host, BoundaryCallbacks callbacks, string tenant) =>
        host.BindAsync(new PluginContext(tenant, "security", "1", "default", JsonSerializer.SerializeToElement(new
        {
        })),
            new DockerProfile("weaveport-poc-security-extended:1", Timeout: TimeSpan.FromSeconds(6)), callbacks, ["read", "slow"]);
    private static long Counter(JsonElement value, string file, string name) => long.Parse(value.GetProperty(file).GetString()!.Split('\n').Single(l => l.StartsWith(name + " ", StringComparison.Ordinal)).Split(' ')[1]);
    private static void Assert(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
    private static async Task<JsonElement> ValueAsync(IPluginSession s, string op, object value)
    {
        InvocationResult result = await s.InvokeAsync(op, JsonSerializer.SerializeToElement(value));
        Assert(result.Status == "ok", "Unexpected status: " + result.Status);
        return result.Value;
    }

    private sealed class BoundaryCallbacks : IHostCallbacks
    {
        internal int Calls;
        internal bool Revoked;
        internal IPluginSession? Target;
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<string> Late { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            if (Revoked)
            {
                throw new UnauthorizedAccessException();
            }

            if (call.Operation == "slow")
            {
                Started.TrySetResult();
                await Release.Task;
                Late.TrySetResult((await Target!.InvokeAsync("echo", JsonSerializer.SerializeToElement(new
                {
                }))).Status);
            }
            return JsonSerializer.SerializeToElement(new
            {
                owner = call.Context.Tenant
            });
        }
    }
}
