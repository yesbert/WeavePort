using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.LocalTools;
using WeavePort.Testing;

internal static class LocalVerification
{
    internal static async Task<int> DoctorAsync(LocalConfiguration config, string output)
    {
        var checks = new List<object>();
        int failures = 0;
        foreach ((string name, string executable) in new[] { ("python", config.Python), ("node", config.Node) })
        {
            try
            {
                if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable)) throw new FileNotFoundException();
                string version = await LocalConfiguration.VersionAsync(executable, "--version");
                Version number = Version.Parse(version.Replace("Python ", "", StringComparison.Ordinal).TrimStart('v'));
                bool supported = name == "python" ? number.Major == 3 && number.Minor >= 14 : number.Major > 24 || number.Major == 24 && number.Minor >= 12;
                if (!supported) throw new NotSupportedException("Runtime version below fixture prerequisite");
                checks.Add(new { name, passed = true, version });
            }
            catch (Exception error) { failures++; checks.Add(new { name, passed = false, error = error.GetType().Name }); }
        }
        foreach (string path in new[] { config.Csharp, config.PythonScript, config.TypeScriptScript })
        {
            bool exists = File.Exists(path);
            if (!exists) failures++;
            checks.Add(new { name = Path.GetFileName(path), passed = exists });
        }
        if (!config.SelfContained)
        {
            try
            {
                string runtimes = await LocalConfiguration.VersionAsync(config.Dotnet, "--list-runtimes");
                if (!runtimes.Contains("Microsoft.NETCore.App 10.", StringComparison.Ordinal)) throw new NotSupportedException();
                checks.Add(new { name = ".NET 10 runtime", passed = true });
            }
            catch (Exception error) { failures++; checks.Add(new { name = ".NET 10 runtime", passed = false, error = error.GetType().Name }); }
        }
        await File.WriteAllTextAsync(Path.Combine(output, "doctor.json"), JsonSerializer.Serialize(new
        {
            utc = DateTimeOffset.UtcNow, os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            hostRuntime = Environment.Version.ToString(), config.SelfContained, config.UseUnixSocket, config.SocketBufferBytes, dockerRequired = false, failures, checks
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Doctor: {failures} failures; Docker not required; trusted processes only.");
        return failures == 0 ? 0 : 1;
    }

    internal static async Task<int> RunAsync(LocalConfiguration config, string output)
    {
        if (await DoctorAsync(config, output) != 0) return 1;
        var checks = new List<object>();
        int failures = 0;
        var callbacks = new Callbacks();
        await using var host = new PluginHost(options: new WorkerPoolOptions(MaintenanceInterval: TimeSpan.FromHours(1)));
        string? oldCanary = Environment.GetEnvironmentVariable("WEAVEPORT_PARENT_CANARY");
        Environment.SetEnvironmentVariable("WEAVEPORT_PARENT_CANARY", "synthetic-parent-only");
        try
        {
            foreach (string language in new[] { "csharp", "python", "typescript" })
            {
                await using IPluginSession a = await BindAsync(language, "A");
                await using IPluginSession b = await BindAsync(language, "B");
                await CheckAsync(language + ": contract and callback", async () => Assert((await CallAsync(a, "search", new { query = "weave" }))[0].GetProperty("id").GetString() == "A-1"));
                await CheckAsync(language + ": environment not inherited", async () => Assert(!(await CallAsync(a, "environment", new { })).GetProperty("parentSecretPresent").GetBoolean()));
                await CheckAsync(language + ": independent workspace and context", async () =>
                {
                    await CallAsync(a, "workspace", new { text = "A-synthetic-private" });
                    Assert((await CallAsync(b, "workspace", new { })).GetString() == "");
                    Assert((await CallAsync(b, "context", new { })).GetProperty("configuration").GetProperty("marker").GetString() == "B-canary");
                });
                await CallAsync(b, "workspace", new { text = "B-private" });
                string victim = b.Instance;
                foreach ((string operation, string status) in new[] { ("crash", "failed"), ("exception", "failed"), ("malformed", "protocol-error"), ("hang", "timeout") })
                    await CheckAsync(language + ": " + operation + " and B continuity", async () =>
                    {
                        InvocationResult result = await a.InvokeAsync(operation, JsonSerializer.SerializeToElement(new { }));
                        Assert(result.Status == status);
                        Assert((await CallAsync(b, "workspace", new { })).GetString() == "B-private" && b.Instance == victim);
                    });
                await CheckAsync(language + ": restart and state replay", async () =>
                {
                    await CallAsync(a, "workspace", new { text = "old" });
                    string old = a.Instance;
                    await a.RestartAsync();
                    Assert((await CallAsync(a, "workspace", new { })).GetString() == "" && a.Instance != old);
                    Assert((await CallAsync(a, "reduce", new { state = 5, amount = 4, now = "2040-01-01" })).GetProperty("state").GetInt32() == 9);
                });
                await CheckAsync(language + ": forged callback denied", async () => Assert((await a.InvokeAsync("callback", JsonSerializer.SerializeToElement(new { operation = "documents.read", args = new { tenant = "B" } }))).Status == "denied"));
                await CheckAsync(language + ": cancellation", async () =>
                {
                    await CallAsync(a, "echo", new { });
                    using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                    Assert((await a.InvokeAsync("delay", JsonSerializer.SerializeToElement(new { ms = 1000 }), cancel.Token)).Status == "cancelled");
                });
            }
            await CheckAsync("untrusted profile rejected before launch", async () =>
            {
                try { await host.BindAsync(Context("X"), config.Profile("python") with { TrustedCode = false }, callbacks, []); }
                catch (NotSupportedException) { return; }
                throw new Exception("Untrusted execution accepted");
            });
            foreach (ExecutionProtections requirement in new[] { ExecutionProtections.RestrictedFileSystem, ExecutionProtections.DisabledNetwork, ExecutionProtections.HardResourceLimits })
                await CheckAsync("unmet protection rejected for bind and prewarm: " + requirement, async () =>
                {
                    int denied = 0;
                    try { await host.BindAsync(Context("X"), config.Profile("python"), callbacks, [], requiredProtection: requirement); } catch (NotSupportedException) { denied++; }
                    try { await host.PrewarmAsync(config.Profile("python"), "1", 1, requiredProtection: requirement); } catch (NotSupportedException) { denied++; }
                    Assert(denied == 2);
                });
            await CheckAsync("missing executable rejected", async () =>
            {
                try { await host.BindAsync(Context("X"), config.Profile("python") with { Executable = Path.Combine(config.WorkspaceRoot, "missing") }, callbacks, []); }
                catch (FileNotFoundException) { return; }
                throw new Exception("Missing prerequisite accepted");
            });
            await CheckAsync("equivalent profiles share pristine reserve without reuse", async () =>
            {
                await host.PrewarmAsync(config.Profile("python"), "1", 1);
                Assert(host.Snapshot.Pristine == 1);
                await using (IPluginSession a = await BindAsync("python", "pristine-A"))
                {
                    await CallAsync(a, "workspace", new { text = "pristine-A-private" });
                    Assert(host.Snapshot.Pristine == 0 && host.Snapshot.Workers == 1);
                }
                await host.PrewarmAsync(config.Profile("python"), "1", 0);
                await using IPluginSession b = await BindAsync("python", "pristine-B");
                Assert((await CallAsync(b, "workspace", new { })).GetString() == "");
            });
            if (config.UseUnixSocket)
                await CheckAsync("incompatible socket worker times out without fallback", async () =>
                {
                    var profile = new ProcessProfile(config.Python, ["-I", "-u", "-c", "import time; time.sleep(30)"], true, config.WorkspaceRoot, timeout: TimeSpan.FromMilliseconds(200)) { UseUnixSocket = true };
                    await using IPluginSession incompatible = await host.BindAsync(Context("incompatible"), profile, callbacks, []);
                    InvocationResult result = await incompatible.InvokeAsync("echo", JsonSerializer.SerializeToElement(new { }));
                    Assert(result.Status == "timeout" && host.Snapshot.Workers == 0);
                });
            await CheckAsync("idle policy destroys used process", async () =>
            {
                await using IPluginSession a = await host.BindAsync(Context("idle"), config.Profile("python", idleTimeout: TimeSpan.FromMilliseconds(50)), callbacks, []);
                await CallAsync(a, "workspace", new { text = "old" });
                string old = a.Instance;
                await Task.Delay(100);
                await host.MaintainAsync();
                Assert((await CallAsync(a, "workspace", new { })).GetString() == "" && a.Instance != old);
            });
        }
        finally { Environment.SetEnvironmentVariable("WEAVEPORT_PARENT_CANARY", oldCanary); }
        await host.DisposeAsync();
        await CheckAsync("owned roots and reservations released", () =>
        {
            Assert(host.Snapshot.Workers == 0 && (!Directory.Exists(config.WorkspaceRoot) || Directory.GetDirectories(config.WorkspaceRoot).Length == 0));
            return Task.CompletedTask;
        });
        await File.WriteAllTextAsync(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(new
        {
            failures, checks, hostAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(PluginHost).Assembly.Location))),
            scope = "Trusted cooperative local processes, no OS sandbox or hard resource ceiling; no native memory-exhaustion attack."
        }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;

        Task<IPluginSession> BindAsync(string language, string tenant) => host.BindAsync(Context(tenant), config.Profile(language, TimeSpan.FromSeconds(2)), callbacks, ["documents.read"]);
        async Task CheckAsync(string name, Func<Task> action)
        {
            try { await action(); checks.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
            catch (Exception error) { failures++; checks.Add(new { name, passed = false, error = error.GetType().Name }); Console.WriteLine("FAIL " + name + ": " + error.GetType().Name); }
        }
    }

    internal static PluginContext Context(string tenant) => new(tenant, "demo", "1", "default", JsonSerializer.SerializeToElement(new { marker = tenant + "-canary" }));
    internal static async Task<JsonElement> CallAsync(IPluginSession session, string operation, object payload) => ContractChecks.Successful(await session.InvokeAsync(operation, JsonSerializer.SerializeToElement(payload)));
    private static void Assert(bool condition) { if (!condition) throw new Exception("Local contract assertion failed"); }
    internal sealed class Callbacks : IHostCallbacks
    {
        public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (call.Operation != "documents.read" || call.Payload.TryGetProperty("tenant", out JsonElement claimed) && claimed.GetString() != call.Context.Tenant) throw new UnauthorizedAccessException();
            return ValueTask.FromResult(JsonSerializer.SerializeToElement(new[] { new { id = call.Context.Tenant + "-1", text = "WeavePort document" } }));
        }
    }
}
