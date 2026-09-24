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

internal static class StreamingMeasurement
{
    internal static async Task<List<object>> RunAsync(string source, string fixtures, string worker,
        string label, string language, bool remote)
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
        await using var local = new LocalPluginClient(await host.BindAsync(new("tenant-0", "streaming-benchmark", "1", "trusted", JsonSerializer.SerializeToElement(new
        {
        })), profile, new NoCallbacks(), []));
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
        var rows = new List<object>();
        foreach (string workload in new[] { "list-64k", "list-8m" })
        {
            var iterations = await MeasureWorkloadAsync(client, workload);
            rows.Add(new
            {
                language,
                remote,
                workload,
                iterations
            });
            Console.Error.WriteLine($"{label}: {language} remote={remote} {workload} complete");
        }
        await gateway.DisposeAsync();
        await app.StopAsync();
        return rows;
    }

    private static async Task<List<object>> MeasureWorkloadAsync(IPluginClient client, string workload)
    {
        await WarmAsync(client, workload);
        var iterations = new List<object>();
        for (int round = 0; round < 6; round++)
        {
            iterations.Add(await MeasureIterationAsync(client, workload));
        }
        return iterations;
    }

    private static async Task WarmAsync(IPluginClient client, string workload)
    {
        for (int warm = 0; warm < 3; warm++)
        {
            Workload.Check(await Workload.ExecuteAsync(client, workload), workload, "tenant-0");
        }
    }

    private static async Task<object> MeasureIterationAsync(IPluginClient client, string workload)
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
        return new
        {
            count,
            elapsedMs = elapsed,
            msPerRequest = elapsed / count,
            hostAllocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated
        };
    }

    private static string FindExecutable(string name) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(directory => Path.Combine(directory, name)).First(File.Exists);
}
