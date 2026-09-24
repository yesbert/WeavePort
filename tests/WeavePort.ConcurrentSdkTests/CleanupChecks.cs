using System.Reflection;
using System.Runtime.CompilerServices;
using WeavePort.Sdk;

internal static class CleanupChecks
{
    private static readonly MethodInfo ExecuteMethod = typeof(PluginApplication).Assembly
        .GetType("WeavePort.Sdk.SessionCleanup", throwOnError: true)!
        .GetMethod("ExecuteAsync", BindingFlags.Static | BindingFlags.NonPublic)!
        .MakeGenericMethod(typeof(int));

    internal static async Task RunAsync()
    {
        await SuccessfulCleanupAsync();
        await SeparateFailuresAsync();
        await CombinedFailuresAsync();
        await CancellationAsync();
        await FatalFailureAsync();
    }

    private static Task<int> ExecuteAsync(Func<Task<int>> action, Func<Task> cleanup) =>
        (Task<int>)ExecuteMethod.Invoke(null, [action, cleanup])!;

    private static async Task SuccessfulCleanupAsync()
    {
        bool cleaned = false;
        int result = await ExecuteAsync(() => Task.FromResult(42), async () =>
        {
            await Task.Yield();
            cleaned = true;
        });
        Check(result == 42 && cleaned, "result waits for asynchronous cleanup");
    }

    private static async Task SeparateFailuresAsync()
    {
        var primary = new InvalidOperationException("Primary fixture failure");
        bool cleaned = false;
        Exception observed = await CaptureAsync(() => ExecuteAsync(() => RaisePrimary(primary), () =>
        {
            cleaned = true;
            return Task.CompletedTask;
        }));
        Check(ReferenceEquals(observed, primary) && cleaned, "primary identity retained after cleanup");
        Check(observed.StackTrace?.Contains(nameof(RaisePrimary), StringComparison.Ordinal) == true,
            "primary throw site remains in stack");

        var secondary = new IOException("Cleanup fixture failure");
        observed = await CaptureAsync(() => ExecuteAsync(() => Task.FromResult(42), () => RaiseCleanup(secondary)));
        Check(ReferenceEquals(observed, secondary), "cleanup-only failure identity retained");
        Check(observed.StackTrace?.Contains(nameof(RaiseCleanup), StringComparison.Ordinal) == true,
            "cleanup throw site remains in stack");
    }

    private static async Task CombinedFailuresAsync()
    {
        var primary = new InvalidOperationException("Primary fixture failure");
        var secondary = new IOException("Cleanup fixture failure");
        Exception observed = await CaptureAsync(() => ExecuteAsync(() => RaisePrimary(primary), () => RaiseCleanup(secondary)));
        var aggregate = observed as AggregateException;
        Check(aggregate?.InnerExceptions.Count == 2
            && ReferenceEquals(aggregate.InnerExceptions[0], primary)
            && ReferenceEquals(aggregate.InnerExceptions[1], secondary), "both causes retained in execution-cleanup order");
        Check(observed.GetType().GetProperty("HasExecutionFailure", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(observed) is true, "combined failure retains execution classification");
    }

    private static async Task CancellationAsync()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        bool cleaned = false;
        Exception observed = await CaptureAsync(() => ExecuteAsync(() => Task.FromCanceled<int>(cancellation.Token), () =>
        {
            cleaned = true;
            return Task.CompletedTask;
        }));
        Check(observed is OperationCanceledException error && error.CancellationToken == cancellation.Token && cleaned,
            "caller cancellation token and cleanup preserved");
    }

    private static async Task FatalFailureAsync()
    {
        // Synthetic exception only; this test never allocates to exhaust memory.
        var fatal = new OutOfMemoryException("Synthetic fatal fixture");
        bool cleaned = false;
        Exception observed = await CaptureAsync(() => ExecuteAsync(() => Task.FromException<int>(fatal), () =>
        {
            cleaned = true;
            throw new IOException("Secondary fixture failure");
        }));
        Check(ReferenceEquals(observed, fatal) && cleaned, "ordinary cleanup failure cannot hide fatal execution failure");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> RaisePrimary(Exception error) => throw error;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task RaiseCleanup(Exception error) => throw error;

    private static async Task<Exception> CaptureAsync(Func<Task<int>> action)
    {
        try
        {
            await action();
        }
        catch (Exception error)
        {
            return error;
        }

        throw new InvalidOperationException("Expected fixture failure was not observed.");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException("FAILED: " + name);
        }

        Console.WriteLine("PASS: " + name);
    }
}
