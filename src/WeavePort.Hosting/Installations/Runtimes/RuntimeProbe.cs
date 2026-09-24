namespace WeavePort.Hosting;

internal static class RuntimeProbe
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    internal const int MaximumOutputCharacters = 65536;
    internal static string Run(string executable, string ecosystem)
    {
        using var probe = new RuntimeProbeProcess(executable, ecosystem);
        if (!probe.Process.WaitForExit((int)Timeout.TotalMilliseconds))
        {
            throw new InvalidDataException("Runtime probe timed out.");
        }

        return probe.Finish();
    }

    internal static async Task<string> RunAsync(string executable, string ecosystem, CancellationToken token)
    {
        using var probe = new RuntimeProbeProcess(executable, ecosystem);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(Timeout);
        try
        {
            await probe.Process.WaitForExitAsync(deadline.Token);
            await probe.ReadersComplete.Task.WaitAsync(deadline.Token);
            return probe.Finish();
        }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        {
            throw new InvalidDataException("Runtime probe timed out.", error);
        }
    }

}
