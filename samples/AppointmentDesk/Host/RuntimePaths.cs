using WeavePort.Hosting;

namespace AppointmentDesk.Host;

internal sealed record RuntimePaths(string Root, string Dotnet, string? SelectedVersion = null, string? Selector = null, string? WorkerRoot = null)
{
    internal string SelectorPath => Selector ?? Path.Combine(Root, "active-version.txt");
    internal InstalledPluginCatalog Catalog => new(Path.Combine(Root, "releases"), new Dictionary<string, string> { ["dotnet"] = Dotnet });

    internal InstalledPlugin Resolve(InstallationIdentity? pin = null)
    {
        if (pin is not null && SelectedVersion is not null && SelectedVersion != pin.Version)
        {
            throw new InvalidDataException("Explicit release conflicts with retained installation.");
        }

        return Catalog.Resolve("appointment-desk", pin?.Version ?? SelectedVersion ?? InstalledPluginCatalog.ReadSelection(SelectorPath), "appointment-desk/v1", pin);
    }

    internal void Activate(string version) => Catalog.Activate(SelectorPath, "appointment-desk", version, "appointment-desk/v1");
}
