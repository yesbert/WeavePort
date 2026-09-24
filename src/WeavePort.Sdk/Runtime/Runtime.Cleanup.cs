using WeavePort.Internal;
using System.Text.Json;

namespace WeavePort.Sdk;
internal sealed partial class Runtime
{
    private static async Task DisposeEnumeratorAsync(IAsyncEnumerator<JsonElement> enumerator)
    {
        try
        {
            await enumerator.DisposeAsync();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            throw new SessionCleanupException([error]);
        }
    }

    private async Task CompleteContextAsync()
    {
        PluginCallContext? context = _context;
        _context = null;
        if (_sessionScope is { } scope)
        {
            scope.Active = false;
            scope.Ready.TrySetResult();
            _sessionScope = null;
        }

        if (context is not null)
        {
            await context.CompleteAsync();
        }
    }

    private async Task ReportFailureAsync(JsonElement request, Exception error, CancellationToken token)
    {
        bool cleanupFailed = error is SessionCleanupException;
        try
        {
            await CloseAsync();
        }
        catch (Exception)
        {
            cleanupFailed = true;
        }

        await _channel.WriteAsync(new { type = FrameKinds.Error, id = request.GetProperty(WireFields.Id).GetString(), code = cleanupFailed ? FailureCodes.CleanupError : FailureCodes.SdkError, primaryCode = error is SessionCleanupException { HasExecutionFailure: false } ? FailureCodes.CleanupError : FailureCodes.SdkError, cleanupFailed }, token);
    }
}
