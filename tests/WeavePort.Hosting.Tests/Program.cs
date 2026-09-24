using System.Text;
using System.Diagnostics;
using System.Text.Json;
using WeavePort.Hosting;

if (args.Contains("--lifecycle-worker"))
{
    await LifecycleChecks.WorkerAsync();
    return;
}
if (args.Contains("--mcp-worker"))
{
    await McpFixture.RunAsync(args.Last());
    return;
}
if (args.Contains("--mcp-benchmark"))
{
    await McpMeasurements.RunAsync(args[^3], args[^2], args[^1]);
    return;
}
if (args.Contains("--mcp-interop"))
{
    await McpChecks.InteropAsync(args[^2], args[^1]);
    return;
}
if (args.Contains("--probe-measure"))
{
    await ProbeMeasurements.RunAsync(args);
    return;
}
if (args.Contains("--framework-oracle"))
{
    FrameworkOracle.Run(args[^1]);
    return;
}
if (args.Contains("--portable"))
{
    await PortableInstallationChecks.RunAsync();
    return;
}
DiagnosticCodeChecks.Run();
await ConsumerFindingChecks.RunAsync();
await PortableInstallationChecks.RunAsync();
Console.WriteLine($"PASS {await McpChecks.RunAsync()} MCP protocol, authority and lifecycle assertions");
ManifestChecks.Run();
await DisposalChecks.RunAsync();
await ShutdownRaceChecks.RunAsync();
await LockOrderChecks.RunAsync();
await DockerCommandChecks.RunAsync();
Console.WriteLine($"PASS {await QuarantineChecks.RunAsync()} quarantine age and reservation assertions");
Console.WriteLine($"PASS {await AdmissionChecks.RunAsync()} concurrent-start admission assertions");
JsonSizeChecks.Run();
await FragmentChecks.RunAsync();
await TransportChecks.RunAsync();
Console.WriteLine($"PASS {await SocketChecks.RunAsync()} socket platform/path/connection assertions");
Console.WriteLine($"PASS {EnvelopeChecks.Run()} hostile envelope assertions");
Console.WriteLine($"PASS {await ProcessSocketChecks.RunAsync()} native endpoint ownership assertions");
Console.WriteLine($"PASS {await LifecycleChecks.RunAsync()} lifecycle and safe diagnostics assertions");
