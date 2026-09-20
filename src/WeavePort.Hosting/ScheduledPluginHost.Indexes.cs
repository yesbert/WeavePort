namespace WeavePort.Hosting;
public sealed partial class ScheduledPluginHost
{
    // Mutations share _sync with registration, dispatch, completion and cleanup.
    // Dormant customer registrations must not become per-invocation scan work.
    private void SetActive(ScheduledPlugin plugin, bool active)
    {
        plugin.Active = active;
        if (active)
        {
            _active.Add(plugin);
        }
        else
        {
            _active.Remove(plugin);
        }
    }

    private void SetResident(ScheduledPlugin plugin, bool resident)
    {
        plugin.Resident = resident;
        if (resident)
        {
            _residents.Add(plugin);
        }
        else
        {
            _residents.Remove(plugin);
        }
    }
}
