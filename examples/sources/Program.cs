using System.Runtime.CompilerServices;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Composition;
using WeavePort.Hosting;
using WeavePort.Sdk;
using WeavePort.Sdk.Client;

if (args.Contains("--worker"))
{
    await new PluginApplication()
        .Source<Download>("download", (input, _, _) => ValueTask.FromResult<Stream>(new RepeatedBytes(input.Bytes)))
        .Stream<int, int>("progress", Progress)
        .RunAsync();
    return;
}

string root = Path.Combine(Path.GetTempPath(), "weaveport-source-example-" + Guid.NewGuid().ToString("N"));
try
{
    await using var host = new PluginHost();
    var context = new PluginContext("example-tenant", "source-example", "1", "trusted-native", JsonSerializer.SerializeToElement(new
    {
    }));
    string executable = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? Environment.ProcessPath!;
    string[] arguments = Path.GetFileNameWithoutExtension(executable) == "dotnet" ? [typeof(Download).Assembly.Location, "--worker"] : ["--worker"];
    var profile = new ProcessProfile(executable, arguments, trustedCode: true, workspaceRoot: Path.Combine(root, "workers"));
    await using var client = new LocalPluginClient(await host.BindAsync(context, profile, new NoCallbacks(), []));
    await using var results = new ResultScope(Path.Combine(root, "results"), context.Tenant);
    ResultHandle result = await Composition.CollectAsync(results, client, "download", JsonSerializer.SerializeToElement(new Download(200L * 1024 * 1024)), chunkBytes: 262144);
    await results.CopyToAsync(result, Stream.Null);
    Console.WriteLine($"Collected and delivered {result.Length:N0} bytes with bounded memory.");
    await foreach (int progress in client.StreamAsync<int, int>("progress", 3))
    {
        Console.WriteLine($"Live progress: {progress}");
    }
}
finally
{
    if (Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }
}

static async IAsyncEnumerable<int> Progress(int count, PluginCallContext context, [EnumeratorCancellation] CancellationToken token)
{
    for (int i = 1; i <= count; i++)
    {
        yield return i;
        await Task.Delay(300, token);
    }
}

internal sealed record Download(long Bytes);
internal sealed class NoCallbacks : IHostCallbacks
{
    public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken) => throw new NotSupportedException();
}
