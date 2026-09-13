namespace WeavePort.Internal;
// Start every independent cleanup action before awaiting completion. Preserve all
// failures, including synchronously throwing implementations of async interfaces.
internal static class Cleanup
{
    internal static async Task RunAsync(params Func<Task>[] actions)
    {
        Task completion = Task.WhenAll(actions.Select(AttemptAsync));
        try
        {
            await completion;
        }
        catch
        {
            if (completion.Exception is { } failures)
            {
                throw failures.Flatten();
            }

            throw;
        }
    }

    private static async Task AttemptAsync(Func<Task> action)
    {
        await action();
    }
}
