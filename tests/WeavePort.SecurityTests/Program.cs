using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;

if (args.Length != 1 || Directory.Exists(args[0])) throw new ArgumentException("New evidence directory required.");
if (TestProfiles.SocketTransport is not null) throw new NotSupportedException("This bounded runner qualifies Docker CLI transport only.");
string output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(output);
var evidence = new Evidence(output);
string before = await ResourceChecks.DockerAsync(["ps", "-q", "--no-trunc"]);
await File.WriteAllTextAsync(Path.Combine(output, "containers-before.txt"), before);
await File.WriteAllTextAsync(Path.Combine(output, "identity.json"), JsonSerializer.Serialize(new
{
    hostingHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(PluginHost).Assembly.Location))),
    fixtureImage = await ResourceChecks.DockerAsync(["image", "inspect", "--format", "{{.Id}}", "weaveport-poc-security-extended:1"]),
    engine = await ResourceChecks.DockerAsync(["info", "--format", "{{json .SecurityOptions}}"]),
    engineVersion = await ResourceChecks.DockerAsync(["version", "--format", "{{.Server.Version}}"]),
    kernel = await ResourceChecks.DockerAsync(["info", "--format", "{{.KernelVersion}}"]),
    runnerHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Pressure).Assembly.Location))),
    limits = new { workers = 2, memoryMiBEach = 256, cpusEach = 0.5, pidsEach = 64, tmpfsMiBEach = 16, operationSeconds = 6 },
    scope = "Bounded Docker CLI fixture verification; no kernel exploits, external targets or daemon reconfiguration."
}));
try
{
    await LanguagePolicy.RunAsync(evidence);
    await SecurityScenarios.RunAsync(evidence);
    for (int repeat = 0; repeat < 2; repeat++) await Pressure.RunAsync(evidence, repeat);
}
finally
{
    string after = await ResourceChecks.DockerAsync(["ps", "-q", "--no-trunc"]);
    await File.WriteAllTextAsync(Path.Combine(output, "containers-after.txt"), after);
    await evidence.Check("Pre-existing containers remain running", () =>
    {
        if (before.Split('\n', StringSplitOptions.RemoveEmptyEntries).Except(after.Split('\n')).Any()) throw new InvalidOperationException("Pre-existing container changed state.");
        return Task.CompletedTask;
    });
    await evidence.FinishAsync();
}
Environment.ExitCode = evidence.Failures == 0 ? 0 : 1;
