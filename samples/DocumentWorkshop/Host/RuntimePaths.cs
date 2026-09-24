using WeavePort.Hosting;
using System.Text.Json;
using DocumentWorkshop.Contracts;
using WeavePort.Abstractions;

namespace DocumentWorkshop.Host;

internal sealed record RuntimePaths(string Root, string Dotnet, string? SelectedVersion = null, string? Selector = null)
{
    internal string SelectorPath => Selector ?? Path.Combine(Root, "active-version.txt");
    internal InstalledPluginCatalog Catalog => new(Path.Combine(Root, "releases"), new Dictionary<string, string> { ["dotnet"] = Dotnet });

    internal InstalledPlugin Resolve(InstallationIdentity? pin = null)
    {
        if (pin is not null && SelectedVersion is not null && SelectedVersion != pin.Version)
        {
            throw new InvalidDataException("Explicit release conflicts with retained installation.");
        }

        return Catalog.Resolve("document-workshop", pin?.Version ?? SelectedVersion ?? InstalledPluginCatalog.ReadSelection(SelectorPath), "document-workshop/v1", pin);
    }

    internal void Activate(string version) => Catalog.Activate(SelectorPath, "document-workshop", version, "document-workshop/v1");
}
