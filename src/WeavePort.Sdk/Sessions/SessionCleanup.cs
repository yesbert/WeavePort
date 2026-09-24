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
        try
        {
            return await action();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            primary = error;
            throw;
        }
        finally
        {
            try
            {
                await cleanup();
            }
            catch (Exception secondary) when (primary is not null && secondary is not OutOfMemoryException)
            {
                throw new SessionCleanupException([primary, secondary])
                {
                    HasExecutionFailure = true
                };
            }
        }
    }
}
