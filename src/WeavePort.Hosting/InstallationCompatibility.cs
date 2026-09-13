using System.Text.Json;

namespace WeavePort.Hosting;
internal sealed record AuthorSdk(string Package, string Version);
internal sealed record CompatibilityDeclaration(int HostApi, int Protocol, Dictionary<string, string> HostPackages, Dictionary<string, AuthorSdk> AuthorSdks);
internal static class InstallationCompatibility
{
    private static readonly CompatibilityDeclaration Supported = Load();
    internal static void Validate(CompatibilityDeclaration? declaration, IEnumerable<string> entries)
    {
        if (declaration is null || declaration.HostApi != Supported.HostApi || declaration.Protocol != Supported.Protocol || declaration.HostPackages is null || declaration.AuthorSdks is null || !declaration.HostPackages.OrderBy(p => p.Key, StringComparer.Ordinal).SequenceEqual(Supported.HostPackages.OrderBy(p => p.Key, StringComparer.Ordinal)) || !declaration.AuthorSdks.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(entries))
        {
            throw new InvalidDataException("Unsupported installation host API, protocol or package declaration.");
        }

        foreach (var sdk in declaration.AuthorSdks)
        {
            if (!Supported.AuthorSdks.TryGetValue(sdk.Key, out var expected) || sdk.Value != expected)
            {
                throw new InvalidDataException("Unsupported installation author SDK.");
            }
        }
    }

    private static CompatibilityDeclaration Load()
    {
        using var stream = typeof(InstallationCompatibility).Assembly.GetManifestResourceStream("WeavePort.LocalCompatibility") ?? throw new InvalidOperationException("Missing packaged compatibility matrix.");
        return JsonSerializer.Deserialize<CompatibilityDeclaration>(stream) ?? throw new InvalidOperationException("Invalid packaged compatibility matrix.");
    }
}
