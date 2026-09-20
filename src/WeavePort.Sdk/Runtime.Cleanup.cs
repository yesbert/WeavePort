using System.Text.Json;

namespace WeavePort.Sdk;
internal sealed partial class Runtime
{
    private async Task CompleteContextAsync()
    {
        PluginCallContext? context = _context;
        _context = null;
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

        await _channel.WriteAsync(new { type = "error", id = request.GetProperty("id").GetString(), code = cleanupFailed ? "cleanup-error" : "sdk-error" }, token);
    }
}
