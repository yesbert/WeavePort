using System.Diagnostics;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;

internal static partial class SharedChecks
{
    internal static async Task RunAsync(string[] args)
    {
        string root = Path.GetFullPath(args.ElementAtOrDefault(0) ?? Environment.CurrentDirectory);
        string python = args.ElementAtOrDefault(1) ?? FindExecutable("python3");
        string node = args.ElementAtOrDefault(2) ?? FindExecutable("node");
        string csharp = Environment.GetEnvironmentVariable("WP_SHARED_CSHARP_FIXTURE") ?? Path.Combine(root, "tests/WeavePort.ConcurrentSdkTests/bin/Release/net10.0/WeavePort.ConcurrentSdkTests.dll");
        foreach (var (language, executable, fixture) in new[] { ("python", python, "shared.py"), ("typescript", node, "shared.mjs"), ("csharp", FindExecutable("dotnet"), csharp) })
        {
            await VerifyLanguageAsync(root, language, executable, fixture);
        }
        await BlockedWriterAsync(root, python);
        await CatalogChecks.RunAsync(root, python, node);
        await BoundedStateChecks.RunAsync(root, python);
        Console.WriteLine("PASS: real-host shared execution, callback identity, cancellation, failure, restart, ownership and shutdown.");

    }
    private static async Task VerifyLanguageAsync(string root, string language, string executable, string fixture)
    {
        Console.WriteLine($"Shared host qualification: {language}");
        List<string> launch = language == "csharp" ? [fixture, "--host-shared"] : [Path.Combine(root, "tests/WeavePort.Shared.Tests/fixtures", fixture)];
        string? sdkPath = language == "csharp" ? null : Environment.GetEnvironmentVariable(language == "python" ? "WP_SHARED_PYTHON_SDK" : "WP_SHARED_NODE_SDK");
        if (sdkPath is not null)
        {
            launch.AddRange(["--sdk-path", sdkPath]);
        }

        var profile = new ProcessProfile(executable, launch, trustedCode: true, reservedMemoryMiB: 128, timeout: TimeSpan.FromSeconds(5)) { ReusePolicy = WorkerReusePolicy.Shared, MaximumCallbacks = 3 };
        await RunCaseAsync(language, "Overlap", () => OverlapAsync(profile));
        await RunCaseAsync(language, "CallbacksAndErrors", () => CallbacksAndErrorsAsync(profile));
        await RunCaseAsync(language, "Cancellation", () => CancellationAsync(profile));
        await RunCaseAsync(language, "CrashRecovery", () => CrashRecoveryAsync(profile));
        await RunCaseAsync(language, "OwnershipAndCoexistence", () => OwnershipAndCoexistenceAsync(profile));
        await RunCaseAsync(language, "Shutdown", () => ShutdownAsync(profile));
        await RunCaseAsync(language, "LargerCallbackBudget", () => LargerCallbackBudgetAsync(profile));
        await RunCaseAsync(language, "CancellationGrace", () => CancellationGraceAsync(profile));
        await RunCaseAsync(language, "CleanupFailure", () => CleanupFailureAsync(profile));
        await RunCaseAsync(language, "Silence", () => SilenceAsync(profile));
        await RunCaseAsync(language, "DetachedCallbackCapacity", () => DetachedCallbackCapacityAsync(profile));
        if (language != "csharp")
        {
            await RunCaseAsync(language, "Malformed", () => MalformedAsync(profile));
        }
    }
    private static async Task RunCaseAsync(string language, string name, Func<Task> execute)
    {
        Console.WriteLine($"BEGIN {language}: {name}");
        await execute();
        Console.WriteLine($"END {language}: {name}");
    }

    private static string FindExecutable(string name) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(directory => Path.Combine(directory, name)).First(File.Exists);
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
    private static PluginContext Context() => new("owner", "shared-fixture", "1", "test", Json(new { }));
    private static PluginHost Host(bool failFast = false) => new(new SchedulingOptions { MaximumWorkers = 3, MaximumHeavyCalls = 1, MaximumPristineWorkers = 0, MemoryBudgetMiB = 512, MaximumCallsPerTenant = 16, QueueTimeout = failFast ? TimeSpan.Zero : TimeSpan.FromSeconds(3) });
    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
    private static async Task WaitAsync(Func<bool> condition, string message)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
        Check(condition(), message);
    }
    private static async Task<string> OutcomeAsync(Task<JsonElement> call)
    {
        try
        {
            await call;
            return "ok";
        }
        catch (PluginCallException error) { return error.Status; }
        catch (OperationCanceledException) { return "cancelled"; }
    }

}
