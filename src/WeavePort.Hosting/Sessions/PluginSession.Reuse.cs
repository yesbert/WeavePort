using WeavePort.Internal;
using System.Text.Json;

namespace WeavePort.Hosting;

internal sealed partial class PluginSession
{
    private bool _clean;
    private bool _freshNext;
    internal bool HasWorker => _worker is not null;
    internal bool LastAcquisitionReused { get; private set; }

    private void ReadCleanupAcknowledgement(JsonElement frame)
    {
        if (binding.Profile.ReusePolicy != WorkerReusePolicy.ApprovedSessions)
        {
            return;
        }

        if (!frame.TryGetProperty(WireFields.Reusable, out JsonElement reusable) || reusable.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            pool.RecordCleanupFailure();
            throw new InvalidDataException("Missing session cleanup acknowledgement.");
        }

        _clean = reusable.GetBoolean();
    }

    private async Task ReturnCleanWorkerAsync()
    {
        if (!_clean || binding.Profile.ReusePolicy != WorkerReusePolicy.ApprovedSessions || _worker is null)
        {
            return;
        }

        Worker worker = _worker;
        _worker = null;
        await pool.ReturnAsync(worker);
    }
}
