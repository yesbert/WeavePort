using System.Diagnostics;
using WeavePort.Abstractions;

namespace AppointmentDesk.Host;
// Explicit sample demonstration/testing only; never part of the plugin contract.
internal static class WorkerFaults
{
    internal static async Task KillAsync(IPluginSession session, CancellationToken token)
    {
        int index = session.Instance.LastIndexOf("-p", StringComparison.Ordinal);
        if (index < 0)
        {
            throw new InvalidOperationException("Missing owned process identity.");
        }

        using var worker = Process.GetProcessById(int.Parse(session.Instance[(index + 2)..]));
        worker.Kill();
        await worker.WaitForExitAsync(token);
    }
}
