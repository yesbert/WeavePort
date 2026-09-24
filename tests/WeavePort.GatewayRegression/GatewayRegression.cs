using System.Text.Json;
using WeavePort.Sdk.Client;
using WeavePort.Sdk.Gateway;

internal static class GatewayRegression
{
    internal static async Task RunAsync(string output, bool control)
    {
        var host = new RegressionHost();
        await host.StartAsync();
        await host.CallAsync(control);
        var schedule = await ForceLateScheduleAsync(host, control);
        string? failure = await ObserveCallFailureAsync(host, control);
        object[] captured = host.Requests.ToArray();
        bool reused = captured.Length >= 2 && ReferenceEquals(captured[0], captured[1]);
        bool persistentSession = !control && captured.Length == 1 && !schedule.ResponseCompleted;
        bool disposedCallCancelled = false;
        object? sessionChecks = null;
        if (!control && failure is null)
        {
            sessionChecks = await SessionChecks.RunAsync(host.Sdk, host.Registry, host.Address, host.Requests);
            disposedCallCancelled = await CheckDisposalAsync(host.Sdk);
        }
        bool passed = control ? failure == "cancelled" && schedule.StaleScheduled && reused : failure is null && persistentSession;
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
        {
            control,
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            sdkHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(RemotePluginClient).Assembly.Location))),
            lateSchedule = true,
            responseCompletedBeforeSchedule = schedule.ResponseCompleted,
            persistentSession,
            staleScheduled = schedule.StaleScheduled,
            writerCompleted = schedule.WriterCompleted,
            queueCount = KestrelState.QueueCount(schedule.Writer),
            sameProducerReused = reused,
            requestCount = captured.Length,
            failure,
            disposedCallCancelled,
            sessionChecks,
            passed
        }));
        Console.WriteLine($"control={control} staleScheduled={schedule.StaleScheduled} reused={reused} failure={failure ?? "none"}");
        await host.StopAsync();
        Environment.Exit(passed ? 0 : 1);
    }

    private static async Task<LateSchedule> ForceLateScheduleAsync(RegressionHost host, bool control)
    {
        if (!host.Requests.TryPeek(out object? producer))
        {
            throw new InvalidOperationException("Missing stream capture.");
        }
        if (control)
        {
            await KestrelState.UntilAsync(() => (bool)KestrelState.Field(producer, "_completedResponse"));
        }
        bool completed = (bool)KestrelState.Field(producer, "_completedResponse");
        object writer = KestrelState.Field(producer, "_frameWriter");
        // Reproduce only the late window-update continuation after response completion.
        // Production code never edits or schedules Kestrel's internal state.
        producer.GetType().GetMethod("Schedule")!.Invoke(producer, null);
        await KestrelState.UntilAsync(() => KestrelState.QueueCount(writer) == 0);
        return new(completed, (bool)KestrelState.Field(producer, "_isScheduled"),
            (bool)KestrelState.Field(writer, "_completed"), writer);
    }

    private static async Task<string?> ObserveCallFailureAsync(RegressionHost host, bool control)
    {
        try
        {
            await host.CallAsync(control);
            return null;
        }
        catch (OperationCanceledException) { return "cancelled"; }
        catch (IOException) { return "io-error"; }
    }

    private static async Task<bool> CheckDisposalAsync(RemotePluginClient client)
    {
        Task pending = ConsumeWaitingAsync(client);
        await Task.Delay(30);
        await client.DisposeAsync();
        try
        {
            await pending.WaitAsync(TimeSpan.FromSeconds(7));
        }
        catch (OperationCanceledException)
        {
            return true;
        }
        throw new InvalidOperationException("Disposed client did not cancel its active stream.");
    }

    private static async Task ConsumeWaitingAsync(IPluginClient client)
    {
        await foreach (var item in client.StreamAsync("wait", JsonSerializer.SerializeToElement(new
        {
        })))
        {
        }
    }

    private sealed record LateSchedule(bool ResponseCompleted, bool StaleScheduled, bool WriterCompleted, object Writer);
}
