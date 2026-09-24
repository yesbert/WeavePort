using System.Diagnostics;
using System.Text.Json;
using WeavePort.Hosting;

string config = Environment.GetEnvironmentVariable("WEAVEPORT_LOCAL_CONFIG") ?? throw new ArgumentException("Set WEAVEPORT_LOCAL_CONFIG to an absolute fixture config path.");
string output = Environment.GetEnvironmentVariable("WEAVEPORT_BULK_OUTPUT") ?? throw new ArgumentException("Set WEAVEPORT_BULK_OUTPUT to a new evidence directory.");
Directory.CreateDirectory(output);
if (args.FirstOrDefault() == "http")
{
    await HttpChecks.RunAsync(config, output);
    return;
}
if (args.FirstOrDefault() == "verify")
{
    await BulkChecks.RunAsync(config, output);
    return;
}
if (args.FirstOrDefault() != "load")
{
    throw new ArgumentException("Expected verify, http or load.");
}

await new BulkLoad(config, output).RunAsync();
