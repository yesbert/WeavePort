namespace WeavePort.Hosting;

internal sealed class TenantAdmission(string tenant, int maximum, Action<TenantAdmission> retain, Action<TenantAdmission> release)
{
    internal string Tenant { get; } = tenant;
    internal SemaphoreSlim Calls { get; } = new(maximum);
    internal SemaphoreSlim Callbacks { get; } = new(maximum);
    internal int References { get; set; }

    internal void RetainCallback() => retain(this);
    internal void ReleaseCallback()
    {
        Callbacks.Release();
        release(this);
    }
}
