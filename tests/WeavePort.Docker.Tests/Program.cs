using System.Diagnostics;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Testing;

string root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
string output = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(root, "reports", DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss"));
Directory.CreateDirectory(output);
var evidence = new Evidence(output);
if (TestProfiles.SocketTransport is not null && OperatingSystem.IsLinux())
    File.SetUnixFileMode("/ipc", UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
await using var host = new PluginHost(4);
await using var external = new ExternalActionService(Path.Combine(output, "external-ledger.json"));
var callbacks = new DemoCallbacks(external);
var languages = new[] { "csharp", "python", "typescript" };
var sessions = new Dictionary<string, IPluginSession>();
foreach (string language in languages)
{
    sessions[language] = await Bind(language, "A", "default");
    IPluginSession session = sessions[language];
    await evidence.Check($"{language}: contract replacement", async () =>
    {
        JsonElement value = Ok(await Call(session, "search", new { query = "weave" }));
        Assert(value.GetArrayLength() == 1 && value[0].GetProperty("id").GetString() == "A-1", "search result");
    });
    await evidence.Check($"{language}: files and subprocess", async () =>
    {
        Assert(Ok(await Call(session, "subprocess", new { })).GetString() == "child-ok", "child output");
        Assert(Ok(await Call(session, "workspace", new { text = "A-private" })).GetString() == "A-private", "workspace");
    });
    await evidence.Check($"{language}: host state replay", async () =>
    {
        JsonElement first = Ok(await Call(session, "reduce", new { state = 3, amount = 2, now = "2040-01-01T00:00:00Z" }));
        string stateFile = Path.Combine(output, language + "-state.json");
        await File.WriteAllTextAsync(stateFile, first.GetRawText());
        await session.RestartAsync();
        using JsonDocument restored = JsonDocument.Parse(await File.ReadAllTextAsync(stateFile));
        first = restored.RootElement;
        JsonElement next = Ok(await Call(session, "reduce", new { state = first.GetProperty("state").GetInt32(), amount = 4, now = "2040-01-01T00:00:00Z" }));
        Assert(next.GetProperty("state").GetInt32() == 9, "state must survive through host replay");
    });
}

await FunctionalScenarios.RunAsync(host, callbacks, evidence);
await AdmissionScenarios.RunAsync(callbacks, evidence);
foreach (string language in languages)
{
    await using IPluginSession b = await Bind(language, "B", "default");
    IPluginSession a = sessions[language];
    await IsolationScenarios.RunAsync(a, b, language, evidence);
}
await ResourceChecks.RunAsync(host, callbacks, evidence, sessions);
await PoolLifecycleScenarios.RunAsync(evidence);
await PoolCleanupScenario.RunAsync(evidence);
await SocketScenarios.RunAsync(evidence);
await SecurityScenarios.RunAsync(evidence);
await evidence.FinishAsync();
Console.WriteLine($"Report: {Path.Combine(output, "report.md")}");
Environment.ExitCode = evidence.Failures == 0 ? 0 : 1;

async Task<IPluginSession> Bind(string language, string tenant, string profile) =>
    await host.BindAsync(new PluginContext(tenant, "demo", "1", profile, JsonSerializer.SerializeToElement(new { secret = tenant + "-test-only" })),
        TestProfiles.Create("weaveport-poc-" + language + ":1", timeout: TimeSpan.FromSeconds(3)), callbacks, DemoCallbacks.Grants);

static Task<InvocationResult> Call(IPluginSession session, string operation, object value) => session.InvokeAsync(operation, JsonSerializer.SerializeToElement(value));
static JsonElement Ok(InvocationResult result) => ContractChecks.Successful(result);
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
