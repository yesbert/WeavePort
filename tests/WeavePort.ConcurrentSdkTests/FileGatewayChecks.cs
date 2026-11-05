using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
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

// Function files through the gateway: readers and writers stay at the remote caller, the registration caps limits.
internal static partial class FileGatewayChecks
{
    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new { });
    private static readonly PluginFileLimits Allowed = new(2, 16 << 20);
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    internal static async Task RunAsync(string[] args)
    {
        string root = Path.GetFullPath(args.ElementAtOrDefault(1) ?? Environment.CurrentDirectory);
        var rows = new List<object>();
        foreach (string language in new[] { "csharp", "python", "typescript" })
        {
            await using var gateway = await GatewayAsync(root, language, 65536);
            await FunctionsAsync(gateway, language);
            Console.WriteLine($"PASS function files through the gateway: {language}");
        }

        await using (var gateway = await GatewayAsync(root, "csharp", 65536))
        {
            await RegistrationCapsAsync(gateway);
            await CommitFailureAsync(gateway);
            await CancellationAsync(gateway);
            await RevocationAsync(gateway);
            await HostTimeoutAsync(gateway);
            await CallerCleanupAsync(gateway);
        }

        await OlderGatewayAsync();
        foreach (int blockBytes in new[] { 65536, 262144 })
        {
            await using var gateway = await GatewayAsync(root, "csharp", blockBytes);
            rows.Add(await MeasureAsync(gateway, blockBytes));
        }

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            remoteLoopback = rows
        }, Indented));
    }

    private static async Task FunctionsAsync(Gateway gateway, string language)
    {
        await using RemotePluginClient client = gateway.Client(Allowed);
        var summary = await client.CallWithFilesAsync("zip-summary", Empty, new PluginFiles([FileChecks.Input()]));
        Check(summary.Value.GetProperty("names").GetArrayLength() == 3, language + " lists the ZIP directory through the gateway");
        var destination = new FileChecks.MemoryDestination();
        var copy = await client.CallWithFilesAsync("copy", Empty, new PluginFiles([FileChecks.Input()], destination, new PluginFileLimits(1, 16 << 20)));
        Check(copy.Outputs.Single().Length == FileChecks.Archive.Length && SHA256.HashData(destination.Committed["copy.bin"]).AsSpan().SequenceEqual(SHA256.HashData(FileChecks.Archive)), language + " writes a byte-exact output into the caller's destination");
        Check(await CodeAsync(() => client.CallWithFilesAsync("leave-open", Empty, new PluginFiles([], new FileChecks.MemoryDestination(), new PluginFileLimits(1, 100)))) == "output-incomplete", language + " reports an output left open through the gateway");
        Check((await client.CallAsync("stats", Empty)).GetProperty("pid").GetInt32() > 0, language + " session stays usable after file calls");
    }

    private static async Task RegistrationCapsAsync(Gateway gateway)
    {
        await using RemotePluginClient none = gateway.Client(null);
        Check(await CodeAsync(() => none.CallWithFilesAsync("zip-summary", Empty, new PluginFiles([FileChecks.Input()])), expectExecuted: false) == "files-unsupported", "a registration without file limits refuses file calls before dispatch");
        await using RemotePluginClient small = gateway.Client(new PluginFileLimits(1, 1 << 20));
        Check(await CodeAsync(() => small.CallWithFilesAsync("copy", Empty, new PluginFiles([FileChecks.Input()], new FileChecks.MemoryDestination(), new PluginFileLimits(1, 16 << 20))), expectExecuted: false) == "output-limit", "output limits above the registration are refused before dispatch");
        await using RemotePluginClient reads = gateway.Client(new PluginFileLimits(1, 1 << 20, 4096));
        Check(await CodeAsync(() => reads.CallWithFilesAsync("zip-summary", Empty, new PluginFiles([FileChecks.Input()], null, new PluginFileLimits(0, 0, 1 << 20))), expectExecuted: false) == "input-read-limit", "a read budget above the registration is refused before dispatch");
        Check(await CodeAsync(() => reads.CallWithFilesAsync("copy", Empty, new PluginFiles([FileChecks.Input()], new FileChecks.MemoryDestination(), new PluginFileLimits(1, 1 << 20)))) == "input-read-limit", "without an explicit budget the registration's budget applies during the call");
        await using RemotePluginClient generous = gateway.Client(new PluginFileLimits(1, 16 << 20, 1L << 30));
        Check(await CodeAsync(() => generous.CallWithFilesAsync("reread", JsonSerializer.SerializeToElement(new { passes = 6 }), new PluginFiles([FileChecks.Input()]))) == "input-read-limit", "without an explicit budget a larger registration cap does not exceed the caller's default");
    }

    private static async Task CommitFailureAsync(Gateway gateway)
    {
        await using RemotePluginClient client = gateway.Client(Allowed);
        var destination = new FailingDestination();
        Check(await CodeAsync(() => client.CallWithFilesAsync("copy", Empty, new PluginFiles([FileChecks.Input()], destination, new PluginFileLimits(1, 16 << 20)))) == "output-commit-failed" && destination.Events.SequenceEqual(["commit", "abort:CommitFailed", "dispose"]), "a failed commit at the caller is aborted and reported");
    }

    private static async Task CancellationAsync(Gateway gateway)
    {
        await using RemotePluginClient client = gateway.Client(Allowed);
        var destination = new FailingDestination();
        var slow = new PluginInput("sheet", FileChecks.Archive.Length, "application/zip", new SlowReader(PluginInputReader.FromStream(new MemoryStream(FileChecks.Archive, writable: false))));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        bool cancelled = false;
        try
        {
            await client.CallWithFilesAsync("copy", Empty, new PluginFiles([slow], destination, new PluginFileLimits(1, 16 << 20)), cancel.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        Check(cancelled && !destination.Events.Contains("commit") && destination.Events.Contains("abort:Cancelled") && destination.Events.Last() == "dispose", "caller cancellation aborts the caller's outputs");
        Check(await RecoversAsync(client), "the binding serves calls once the cancelled file call is cleaned up");
    }

    // A gateway without function files answers FILE_CALL as an unknown mode, like the 0.9.1 server's default branch.
    private static async Task OlderGatewayAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2));
        builder.Services.AddGrpc();
        await using WebApplication app = builder.Build();
        app.MapGrpcService<OlderGateway>();
        await app.StartAsync();
        await using var client = new RemotePluginClient(new Uri(app.Urls.Single()), "credential");
        PluginCallException? error = null;
        try
        {
            await client.CallWithFilesAsync("copy", Empty, new PluginFiles([FileChecks.Input()]));
        }
        catch (PluginCallException failure)
        {
            error = failure;
        }

        Check(error is { ErrorCode: "files-unsupported", MayHaveExecuted: false } && error.Failure?.Code == "files-unsupported", "an older gateway is reported as files-unsupported in status and structured code");
        await app.StopAsync();
    }

    private static async Task CallerCleanupAsync(Gateway gateway)
    {
        await using RemotePluginClient client = gateway.Client(Allowed);
        var destination = new DisposalFailingDestination();
        PluginCallException? error = null;
        try
        {
            await client.CallWithFilesAsync("copy", Empty, new PluginFiles([FileChecks.Input()], destination, new PluginFileLimits(1, 16 << 20)));
        }
        catch (PluginCallException failure)
        {
            error = failure;
        }

        Check(error is { ErrorCode: "cleanup-error", Failure.CleanupFailed: true } && error.Outputs.Single().Length == FileChecks.Archive.Length, "a caller-side writer release failure is reported with the committed output");
    }

    private static async Task HostTimeoutAsync(Gateway gateway)
    {
        await using RemotePluginClient client = gateway.Client(Allowed, TimeSpan.FromSeconds(1));
        var destination = new FailingDestination();
        var slow = new PluginInput("sheet", FileChecks.Archive.Length, "application/zip", new SlowReader(PluginInputReader.FromStream(new MemoryStream(FileChecks.Archive, writable: false))));
        string code = await CodeAsync(() => client.CallWithFilesAsync("copy", Empty, new PluginFiles([slow], destination, new PluginFileLimits(1, 16 << 20))));
        Check(code == "timeout" && !destination.Events.Contains("commit") && destination.Events.Last() == "dispose", $"a host timeout during a relayed read reaches the caller as timeout ({code})");
        Check(await RecoversAsync(client), "the client opens a fresh session after the relay ended the old one");
    }

    private static async Task RevocationAsync(Gateway gateway)
    {
        await using RemotePluginClient client = gateway.Client(Allowed);
        var destination = new FailingDestination();
        var slow = new PluginInput("sheet", FileChecks.Archive.Length, "application/zip", new SlowReader(PluginInputReader.FromStream(new MemoryStream(FileChecks.Archive, writable: false))));
        // Revocation cancels the server-side invocation; like a plain gateway call, the client reports it as cancellation.
        Task<string> call = CancelledOrCodeAsync(() => client.CallWithFilesAsync("copy", Empty, new PluginFiles([slow], destination, new PluginFileLimits(1, 16 << 20))));
        await Task.Delay(400);
        await gateway.RevokeLastAsync();
        string code = await call.WaitAsync(TimeSpan.FromSeconds(30));
        Check(code != "ok" && !destination.Events.Contains("commit") && destination.Events.Last() == "dispose", $"revoking the credential during a file call fails it and aborts the caller's outputs ({code})");
    }

    // The server finishes tearing down the cancelled invocation asynchronously; until then the binding reports busy.
    private static async Task<bool> RecoversAsync(RemotePluginClient client)
    {
        long started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(5))
        {
            try
            {
                return (await client.CallAsync("stats", Empty)).GetProperty("pid").GetInt32() > 0;
            }
            catch (PluginCallException error) when (error.ErrorCode == "busy" && !error.MayHaveExecuted)
            {
                await Task.Delay(100);
            }
        }

        return false;
    }

    private static async Task<object> MeasureAsync(Gateway gateway, int blockBytes)
    {
        await using RemotePluginClient client = gateway.Client(Allowed);
        await client.CallWithFilesAsync("copy", Empty, new PluginFiles([FileChecks.Input()], new FileChecks.MemoryDestination(), new PluginFileLimits(1, 16 << 20)));
        var copies = new List<double>();
        for (int run = 0; run < 5; run++)
        {
            long started = Stopwatch.GetTimestamp();
            await client.CallWithFilesAsync("copy", Empty, new PluginFiles([FileChecks.Input()], new FileChecks.MemoryDestination(), new PluginFileLimits(1, 16 << 20)));
            copies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        double median = copies.Order().ElementAt(2);
        int blocks = 2 * (int)Math.Ceiling(FileChecks.Archive.Length / (double)blockBytes);
        return new
        {
            language = "csharp",
            blockBytes,
            copyMedianMs = Math.Round(median, 1),
            copyMiBPerSecond = Math.Round(FileChecks.Archive.Length / 1048576.0 / (median / 1000), 1),
            roundTrips = blocks,
            msPerRoundTrip = Math.Round(median / blocks, 3)
        };
    }

    private static async Task<Gateway> GatewayAsync(string root, string language, int blockBytes)
    {
        var host = new PluginHost();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2));
        builder.Services.AddWeavePortGateway();
        WebApplication app = builder.Build();
        app.MapWeavePortGateway();
        await app.StartAsync();
        return new Gateway(host, app, root, language, blockBytes);
    }

    private static async Task<string> CancelledOrCodeAsync(Func<Task> action)
    {
        try
        {
            return await CodeAsync(action);
        }
        catch (OperationCanceledException)
        {
            return "cancelled";
        }
    }

    private static async Task<string> CodeAsync(Func<Task> action, bool expectExecuted = true)
    {
        try
        {
            await action();
            return "ok";
        }
        catch (PluginCallException error) when (expectExecuted || !error.MayHaveExecuted)
        {
            return error.ErrorCode;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("FAIL: " + message);
        }

        Console.WriteLine("PASS: " + message);
    }
}
