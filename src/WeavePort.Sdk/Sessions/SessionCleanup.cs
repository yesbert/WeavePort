using System.Runtime.ExceptionServices;

namespace WeavePort.Sdk;

internal static class SessionCleanup
{
    internal static async Task RunAsync(params Func<Task>[] actions)
    {
        List<Exception> errors = [];
        foreach (var action in actions)
        {
            try
            {
                await action();
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                errors.Add(error);
            }
        }

        if (errors.Count != 0)
        {
            throw new SessionCleanupException(errors);
        }
    }

    internal static async Task<T> ExecuteAsync<T>(Func<Task<T>> action, Func<Task> cleanup)
    {
        Exception? primary = null;
        Exception? secondary = null;
        T? result = default;
        try
        {
            result = await action();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            primary = error;
        }
        finally
        {
            // Cleanup still runs for fatal execution errors; an ordinary secondary
            // error must not replace the fatal exception already unwinding.
            try
            {
                await cleanup();
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                secondary = error;
            }
        }

        if (primary is not null && secondary is not null)
        {
            throw new SessionCleanupException([primary, secondary])
            {
                HasExecutionFailure = true
            };
        }

        if (primary is not null)
        {
            ExceptionDispatchInfo.Throw(primary);
        }

        if (secondary is not null)
        {
            ExceptionDispatchInfo.Throw(secondary);
        }

        return result!;
    }
}
