using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace WeavePort.Hosting;
/// <summary>One bounded, best-effort diagnostic queue per host. Logger latency never blocks a worker pipe.</summary>
internal sealed class WorkerDiagnostics
{
    private const int MaximumPendingLines = 128;
    private readonly Channel<(string Instance, string Line)> _lines = Channel.CreateBounded<(string, string)>(
        new BoundedChannelOptions(MaximumPendingLines)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            AllowSynchronousContinuations = false
        });
    internal WorkerDiagnostics(ILogger logger)
    {
        _ = Task.Run(() => DeliverAsync(logger));
    }

    private async Task DeliverAsync(ILogger logger)
    {
        try
        {
            await foreach (var entry in _lines.Reader.ReadAllAsync())
            {
                RuntimeLog.StandardError(logger, entry.Instance, entry.Line);
            }
        }
        catch (Exception)
        {
            // Caller-owned logging can fail. Disable optional delivery without failing plugin execution.
            Complete();
        }
    }

    internal void Write(string instance, string line) => _lines.Writer.TryWrite((instance, line));
    internal void Complete() => _lines.Writer.TryComplete();
}
