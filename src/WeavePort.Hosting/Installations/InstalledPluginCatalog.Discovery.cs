using static WeavePort.Hosting.InstallationFiles;

namespace WeavePort.Hosting;
public sealed partial class InstalledPluginCatalog
{
    /// <summary>Reports every immediate plugin directory, including unselected or refused installations. Root enumeration errors propagate. An optional contract restricts usable results without hiding mismatches.</summary>
    public IReadOnlyList<InstallationDiscovery> ListAll(string? contract = null)
    {
        var result = new List<InstallationDiscovery>();
        foreach (string directory in DiscoveryDirectories(contract))
        {
            try
            {
                InstalledPlugin installation = DiscoverContent(directory, contract);
                installation.RuntimeValidation?.Validate();
                result.Add(new(directory, installation, null, null));
            }
            catch (Exception error) when (DiscoveryFailure(error))
            {
                result.Add(Refused(directory, error));
            }
        }

        return result.AsReadOnly();
    }

    /// <summary>Reports every immediate plugin directory with cancellable runtime validation. Cancellation and root enumeration errors propagate.</summary>
    public async Task<IReadOnlyList<InstallationDiscovery>> ListAllAsync(string? contract = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new List<InstallationDiscovery>();
        foreach (string directory in DiscoveryDirectories(contract))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                InstalledPlugin installation = DiscoverContent(directory, contract);
                await ValidateDiscoveredRuntimeAsync(installation, cancellationToken);
                result.Add(new(directory, installation, null, null));
            }
            catch (Exception error) when (DiscoveryFailure(error))
            {
                result.Add(Refused(directory, error));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return result.AsReadOnly();
    }

    private IEnumerable<string> DiscoveryDirectories(string? contract)
    {
        if (contract is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(contract);
        }

        // Enumerate directly: Directory.Exists would hide permission and I/O failures.
        return Directory.EnumerateDirectories(Path.GetFullPath(releases)).Order(StringComparer.Ordinal);
    }

    private InstalledPlugin DiscoverContent(string directory, string? contract)
    {
        RejectLink(directory);
        string selector = Path.Combine(directory, "active.txt");
        if (!File.Exists(selector))
        {
            throw new FileNotFoundException("Plugin directory has no readable active.txt selector.", selector);
        }

        string version = ReadSelection(selector);
        string nested = Path.Combine(directory, "releases");
        RejectLink(nested);
        string root = Path.Combine(nested, version);
        (Manifest manifest, _) = ReadManifest(root, Path.Combine(root, "installation.json"));
        if (string.IsNullOrWhiteSpace(manifest.Contract) || (contract is not null && manifest.Contract != contract))
        {
            throw new InvalidDataException("Selected installation does not declare the expected contract.");
        }

        return ResolveContent(Path.GetFileName(directory), version, manifest.Contract, null);
    }

    private static bool DiscoveryFailure(Exception error) => error is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or FormatException or NotSupportedException;
    private static InstallationDiscovery Refused(string directory, Exception error) => new(directory, null, error switch
    {
        FileNotFoundException => "missing-file",
        UnauthorizedAccessException => "access-denied",
        InvalidDataException or ArgumentException or FormatException or NotSupportedException => "invalid-installation",
        _ => "io-error"
    }, error.Message);
    private static async Task ValidateDiscoveredRuntimeAsync(InstalledPlugin installation, CancellationToken token)
    {
        if (installation.RuntimeValidation is { } validation)
        {
            await validation.ValidateAsync(token);
        }
    }
}
