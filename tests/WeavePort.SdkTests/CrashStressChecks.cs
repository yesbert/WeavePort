using System.Collections.Concurrent;
using System.Text.Json;
using WeavePort.Sdk.Client;
using WeavePort.SdkFixture;

internal static class CrashStressChecks
{
    internal static async Task RunAsync()
    {
        await using var harness = await Harness.CreateAsync(["python", "python", "python"], false);
        var failures = new ConcurrentQueue<Exception>();
        int completed = 0;
        await Task.WhenAll(harness.Clients.Select(async client =>
        {
            for (int i = 0; i < 2000 && failures.IsEmpty; i++)
            {
                try
                {
                    await VerifyCrashAsync(client);
                    Interlocked.Increment(ref completed);
                }
                catch (Exception error)
                {
                    failures.Enqueue(error);
                }
            }
        }));
        if (failures.TryPeek(out var failure))
        {
            Console.Error.WriteLine($"Unexpected crash outcome after {completed} calls: {failure.GetType().Name}; status={(failure as PluginCallException)?.Status}");
            Console.Error.WriteLine(failure.StackTrace);
            throw new Exception("Crash regression failed");
        }
        Console.WriteLine($"PASS {completed} repeated crash/replacement assertions across three Python tenants");
    }
    private static async Task VerifyCrashAsync(IPluginClient client)
    {
        var echo = await client.CallAsync("echo", JsonSerializer.SerializeToElement(new
        {
            sentinel = 42
        }));
        if (echo.GetProperty("sentinel").GetInt32() != 42)
        {
            throw new Exception("Replacement echo mismatch");
        }

        try
        {
            await client.CallAsync("crash", JsonSerializer.SerializeToElement(new
            {
            }));
            throw new Exception("Crash unexpectedly succeeded");
        }
        catch (PluginCallException error) when (error.Status == "failed")
        {
            // Expected failure confirms this replacement cycle.
        }
    }

}
