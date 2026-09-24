using System.Text.Json;

namespace WeavePort.Hosting;

internal static class InstallationCompatibility
{
    private static readonly CompatibilityDeclaration Supported = Load();
    internal static void Validate(CompatibilityDeclaration? declaration, IEnumerable<string> entries)
    {
        if (declaration is null || declaration.HostApi != Supported.HostApi ||
            !(Supported.Protocols ?? [Supported.Protocol]).Contains(declaration.Protocol))
        {
            throw new InvalidDataException("Unsupported installation host API, protocol or package declaration.");
        }

        if (declaration.HostPackages is null || declaration.AuthorSdks is null ||
            !declaration.HostPackages.OrderBy(package => package.Key, StringComparer.Ordinal)
                .SequenceEqual(Supported.HostPackages.OrderBy(package => package.Key, StringComparer.Ordinal)) ||
            !declaration.AuthorSdks.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(entries))
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

    internal static CompatibilityDeclaration Create(IEnumerable<string> entries, PluginLaunchDeclaration launch)
    {
        int protocol = launch.Ownership.Contains(WorkerReusePolicy.Shared) ? 2 : 1;
        var sdks = entries.ToDictionary(alias => alias, SupportedAuthorSdk, StringComparer.Ordinal);
        var result = Supported with
        {
            Protocol = protocol,
            Protocols = null,
            AuthorSdks = sdks
        };
        Validate(result, sdks.Keys);
        return result;
    }

    private static AuthorSdk SupportedAuthorSdk(string alias)
    {
        if (!Supported.AuthorSdks.TryGetValue(alias, out AuthorSdk? sdk))
        {
            throw new InvalidDataException("Unsupported author SDK alias.");
        }

        return sdk;
    }

    private static CompatibilityDeclaration Load()
    {
        using var stream = typeof(InstallationCompatibility).Assembly.GetManifestResourceStream("WeavePort.LocalCompatibility") ?? throw new InvalidOperationException("Missing packaged compatibility matrix.");
        return JsonSerializer.Deserialize<CompatibilityDeclaration>(stream) ?? throw new InvalidOperationException("Invalid packaged compatibility matrix.");
    }
}
