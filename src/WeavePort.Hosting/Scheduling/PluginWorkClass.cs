namespace WeavePort.Hosting;
/// <summary>Host-authorized execution class; never select it from untrusted request data.</summary>
public enum PluginWorkClass
{
    /// <summary>Short, reconstructible work.</summary>
    Normal,
    /// <summary>Explicitly admitted long-running work.</summary>
    Heavy
}
