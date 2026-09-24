using System.Globalization;
using System.Text.Json;
using WeavePort.CapacityTests;
using WeavePort.Hosting;
using WeavePort.Runner;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
if (args.Length > 0 && args[0] == "--diagnostic-report") { await DiagnosticReport.WriteAsync(args[1]); return; }
if (args.Length > 0 && args[0] == "--linux-report") { await LinuxReport.WriteAsync(args[1]); return; }
if (args.Length > 0 && args[0] == "--linux-probe") { await LinuxProbe.RunAsync(args[1]); return; }
if (args.SequenceEqual(new[] { "--self-test" })) { InstrumentationChecks.Run(); return; }
if (args.SequenceEqual(new[] { "--restart-test" })) { await InstrumentationChecks.RunRestartAsync(); return; }
await CapacityCommand.RunAsync(CapacitySettings.Parse(args));
