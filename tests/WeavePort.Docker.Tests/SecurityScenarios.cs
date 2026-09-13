using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Testing;

internal static class SecurityScenarios
{
    internal static async Task RunAsync(Evidence evidence)
    {
        await using var host = new PluginHost();
        var callbacks = new ObjectCallbacks();
        await using IPluginSession b = await BindAsync("security-B");
        await InvokeAsync(b, "workspace", new { text = "B-synthetic-private" });
        string victim = b.Instance;
        await SecurityObservations.RunAsync(evidence, "adversarial-python", b.Instance);

        await evidence.Check("security: foreign writable state and used-worker replacement", async () =>
        {
            string old;
            await using (IPluginSession a = await BindAsync("security-A"))
            {
                Assert((await InvokeAsync(a, "workspace", new { })).GetString() == "", "Foreign marker visible");
                await InvokeAsync(a, "workspace", new { text = "A-synthetic-private" });
                old = a.Instance;
                await VictimAsync();
            }
            await using IPluginSession replacement = await BindAsync("security-C");
            Assert((await InvokeAsync(replacement, "workspace", new { })).GetString() == "" && replacement.Instance != old, "Used state reassigned");
            Assert((await InvokeAsync(replacement, "context", new { })).GetProperty("tenant").GetString() == "security-C", "Stale authority");
            await VictimAsync();
        });
        await evidence.Check("security: control paths, root write, root identity and network denied", async () =>
        {
            await using IPluginSession a = await BindAsync("security-A");
            JsonElement probes = await InvokeAsync(a, "probe", new { });
            await File.WriteAllTextAsync(Path.Combine(evidence.DirectoryPath, "security-probes.json"), probes.GetRawText());
            foreach (JsonProperty probe in probes.EnumerateObject())
            {
                Assert(probe.Value.ValueKind == JsonValueKind.Number, "Unexpected successful operation: " + probe.Name);
                int error = probe.Value.GetInt32();
                bool expected = probe.Name switch
                {
                    "rootWrite" => error == 30, // EROFS
                    "setuid" => error == 1, // EPERM
                    "externalConnect" => error is 101 or 113, // no route; timeout is not proof of isolation
                    _ => error is 2 or 13 // absent control path or permission denied
                };
                Assert(expected, "Unexpected probe outcome: " + probe.Name + "=" + error);
            }
            await VictimAsync();
        });
        await evidence.Check("security: forged envelope authority cannot replace bound customer", async () =>
        {
            await using IPluginSession a = await BindAsync("security-A");
            JsonElement value = await InvokeAsync(a, "callback", new { operation = "objects.read", args = new { objectId = "doc-A" } });
            Assert(value.GetProperty("owner").GetString() == "security-A" && value.GetProperty("text").GetString() == "A-owned-document", "Envelope replaced binding");
            JsonElement context = await InvokeAsync(a, "context", new { });
            Assert(context.GetProperty("configuration").GetProperty("marker").GetString() == "security-A-config-canary" &&
                !context.GetRawText().Contains("security-B-config-canary", StringComparison.Ordinal), "Foreign configuration visible");
            await VictimAsync();
        });
        foreach (object arguments in new object[] { new { objectId = "doc-B" }, new { objectId = "doc-A", tenant = "security-B" } })
        {
            await evidence.Check("security: object ownership and tenant argument independently enforced " + JsonSerializer.Serialize(arguments), async () =>
            {
                await using IPluginSession a = await BindAsync("security-A");
                InvocationResult result = await a.InvokeAsync("callback", JsonSerializer.SerializeToElement(new { operation = "objects.read", args = arguments }));
                Assert(result.Status == "denied" && !result.Value.GetRawText().Contains("B-owned-document", StringComparison.Ordinal), "Foreign object accepted");
                await VictimAsync();
            });
        }
        await evidence.Check("security: absent callback grant prevents application dispatch", async () =>
        {
            await using IPluginSession a = await BindAsync("security-A", grants: []);
            int before = callbacks.Calls;
            InvocationResult result = await a.InvokeAsync("callback", JsonSerializer.SerializeToElement(new { operation = "objects.read", args = new { objectId = "doc-A" } }));
            Assert(result.Status == "denied" && callbacks.Calls == before, "Unapproved callback dispatched");
            await VictimAsync();
        });
        foreach ((string name, string frame) in HostileFrames())
        {
            await evidence.Check("security: reject " + name + " before callback with victim continuity", async () =>
            {
                await using IPluginSession a = await BindAsync("security-A");
                int before = callbacks.Calls;
                InvocationResult result = await a.InvokeAsync("hostile", JsonSerializer.SerializeToElement(new { frame }));
                Assert(result.Status == "protocol-error" && callbacks.Calls == before, "Hostile frame escaped bounded protocol failure");
                await VictimAsync();
            });
        }
        await evidence.Check("security: out-of-range ready version is a cleaned-up protocol failure", async () =>
        {
            int before = host.Snapshot.Workers;
            await using IPluginSession a = await BindAsync("security-A", "weaveport-poc-security-badready:1");
            InvocationResult result = await a.InvokeAsync("echo", JsonSerializer.SerializeToElement(new { }));
            Assert(result.Status == "protocol-error" && host.Snapshot.Workers == before, "Invalid ready leaked exception or worker");
            await VictimAsync();
        });
        await b.DisposeAsync();
        await evidence.Check("security: owned test workers released", () =>
        {
            Assert(host.Snapshot.Workers == 0, "Security workers retained");
            return Task.CompletedTask;
        });

        Task<IPluginSession> BindAsync(string tenant, string image = "weaveport-poc-security:1", string[]? grants = null) => host.BindAsync(
            new PluginContext(tenant, "security-fixture", "1", "default", JsonSerializer.SerializeToElement(new { marker = tenant + "-config-canary" })),
            TestProfiles.Create(image, timeout: TimeSpan.FromSeconds(3)), callbacks, grants ?? ["objects.read"]);
        async Task VictimAsync()
        {
            Assert((await InvokeAsync(b, "workspace", new { })).GetString() == "B-synthetic-private" && b.Instance == victim, "Victim state or instance changed");
        }
    }

    private static (string Name, string Frame)[] HostileFrames() =>
    [
        ("scalar frame", "42"), ("missing identity", "{\"type\":\"result\",\"value\":0}"),
        ("wrong identity", "{\"type\":\"result\",\"id\":\"foreign\",\"value\":0}"),
        ("numeric identity", "{\"type\":\"result\",\"id\":42,\"value\":0}"),
        ("duplicate identity", "{\"type\":\"result\",\"id\":\"foreign\",\"id\":\"$id\",\"value\":0}"),
        ("escaped duplicate identity", "{\"type\":\"result\",\"id\":\"foreign\",\"\\u0069d\":\"$id\",\"value\":0}"),
        ("duplicate operation", "{\"type\":\"callback\",\"id\":\"$id\",\"callbackId\":\"one\",\"operation\":\"admin\",\"operation\":\"objects.read\",\"payload\":{\"objectId\":\"doc-A\"}}"),
        ("duplicate payload", "{\"type\":\"callback\",\"id\":\"$id\",\"callbackId\":\"one\",\"operation\":\"objects.read\",\"payload\":{\"objectId\":\"doc-B\"},\"payload\":{\"objectId\":\"doc-A\"}}"),
        ("unknown frame type", "{\"type\":\"admin\",\"id\":\"$id\"}")
    ];

    private static async Task<JsonElement> InvokeAsync(IPluginSession session, string operation, object payload) =>
        ContractChecks.Successful(await session.InvokeAsync(operation, JsonSerializer.SerializeToElement(payload)));
    private static void Assert(bool condition, string reason) { if (!condition) throw new Exception(reason); }

    private sealed class ObjectCallbacks : IHostCallbacks
    {
        internal int Calls { get; private set; }
        private readonly Dictionary<string, (string Owner, string Text)> _objects = new()
        { ["doc-A"] = ("security-A", "A-owned-document"), ["doc-B"] = ("security-B", "B-owned-document") };

        public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            if (call.Operation != "objects.read" ||
                (call.Payload.TryGetProperty("tenant", out JsonElement claimed) && claimed.GetString() != call.Context.Tenant) ||
                !call.Payload.TryGetProperty("objectId", out JsonElement id) || id.ValueKind != JsonValueKind.String ||
                !_objects.TryGetValue(id.GetString()!, out var value) || value.Owner != call.Context.Tenant)
                throw new UnauthorizedAccessException();
            return ValueTask.FromResult(JsonSerializer.SerializeToElement(new { owner = value.Owner, text = value.Text }));
        }
    }
}
