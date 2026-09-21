using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;
using WeavePort.Sdk.Gateway;
using WeavePort.SdkFixture;

string source = Path.GetFullPath(args[0]);
string fixtures = Path.GetFullPath(args[1]);
string worker = Path.GetFullPath(args[2]);
string label = args[3];
string output = Path.GetFullPath(args[4]);
var rows = new List<object>();
string[] languages = Environment.GetEnvironmentVariable("WP_STREAM_LANGUAGE") is { Length: > 0 } selected ? [selected] : ["csharp", "python", "typescript"];
foreach (string language in languages)
foreach (bool remote in new[] { false, true })
{
    await using var host = new PluginHost();
    string executable = FindExecutable(language == "csharp" ? "dotnet" : language == "python" ? "python3" : "node");
    string[] arguments = language switch
    {
        "csharp" => [worker],
        "python" => [Path.Combine(fixtures, "worker.py"), Path.Combine(source, "sdks/python"), Path.Combine(source, "examples/sdk/python/plugin.py")],
        _ => [Path.Combine(fixtures, "worker.mjs"), Path.Combine(source, "sdks/typescript/dist/index.js")]
    };
    var profile = new ProcessProfile(executable, arguments, trustedCode: true);
    await using var local = new LocalPluginClient(await host.BindAsync(new("tenant-0", "streaming-benchmark", "1", "trusted", JsonSerializer.SerializeToElement(new { })), profile, new NoCallbacks(), []));
    var builder = WebApplication.CreateSlimBuilder();
    builder.Logging.ClearProviders();
    builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2));
    builder.Services.AddWeavePortGateway();
    await using var app = builder.Build();
    app.MapWeavePortGateway();
    await app.StartAsync();
    string credential = app.Services.GetRequiredService<GatewayRegistry>().Register(local);
    await using var gateway = new RemotePluginClient(new Uri(app.Urls.Single()), credential);
    IPluginClient client = remote ? gateway : local;
    foreach (string workload in new[] { "list-64k", "list-8m" })
    {
        for (int warm = 0; warm < 3; warm++) Workload.Check(await Workload.ExecuteAsync(client, workload), workload, "tenant-0");
        var iterations = new List<object>();
        for (int round = 0; round < 6; round++)
        {
            int count = 0;
            long allocated = GC.GetTotalAllocatedBytes(true);
            long began = Stopwatch.GetTimestamp();
            do
            {
                Workload.Check(await Workload.ExecuteAsync(client, workload), workload, "tenant-0");
                count++;
            }
            while (Stopwatch.GetElapsedTime(began).TotalMilliseconds < 250);
            double elapsed = Stopwatch.GetElapsedTime(began).TotalMilliseconds;
            iterations.Add(new { count, elapsedMs = elapsed, msPerRequest = elapsed / count, hostAllocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated });
        }
        rows.Add(new { language, remote, workload, iterations });
        Console.Error.WriteLine($"{label}: {language} remote={remote} {workload} complete");
    }
    await gateway.DisposeAsync();
    await app.StopAsync();
}
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { label, source, sdkRuntime = Environment.Version.ToString(), os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, cores = Environment.ProcessorCount, serverGc = System.Runtime.GCSettings.IsServerGC, sourceBuilt = true, gatewaySameProcess = true, validation = "existing SDK Workload.Check over every result", rows }, new JsonSerializerOptions { WriteIndented = true }));

static string FindExecutable(string name) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(directory => Path.Combine(directory, name)).First(File.Exists);
internal sealed class NoCallbacks : IHostCallbacks
{
    public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken) => throw new NotSupportedException();
}
