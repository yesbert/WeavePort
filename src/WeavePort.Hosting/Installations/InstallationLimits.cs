namespace WeavePort.Hosting;

internal static class InstallationLimits
{
    internal const int ManifestBytes = 1048576;
    internal const int Files = 4096;
    internal const int EntryPoints = 32;
    internal const int ReleaseIdentifierCharacters = 64;
    internal const int InventoryEntries = 8192;
    internal const int SelectorBytes = 128;
    internal const int JsonDepth = 8;
}
