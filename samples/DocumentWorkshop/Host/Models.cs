using WeavePort.Hosting;
using System.Text.Json;
using DocumentWorkshop.Contracts;
using WeavePort.Abstractions;

namespace DocumentWorkshop.Host;
internal static class Json
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}

internal sealed record WorkshopConfiguration(string[] InstalledReaders);
internal sealed record ImportOptions(string File, string Store, string Tenant = "demo", string Profile = "default", string? Reader = null);
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

internal sealed record ImportResult(string Path, string Reader, int Sections, int Fragments, long SourceBytes, int ReadCalls, int MaximumRead, string[] DeclinedReaders);
internal sealed record TestHooks(Func<IPluginSession, SourceLease, Task>? Bound = null, Func<ExtractionPage, ExtractionPage>? TransformPage = null, Func<IPluginSession, ExtractionPage, Task>? PageReceived = null, bool DenyRead = false);
internal sealed class Catalog(WorkshopConfiguration configuration)
{
    internal ReaderInfo[] Installed()
    {
        if (configuration.InstalledReaders is null || configuration.InstalledReaders.Distinct().Count() != configuration.InstalledReaders.Length)
        {
            throw new InvalidDataException("Invalid reader configuration.");
        }

        return configuration.InstalledReaders.Select(id => id switch
        {
            "markdown" => new ReaderInfo(id, "1", ["text/markdown"]),
            "plain" => new ReaderInfo(id, "1", ["text/markdown", "text/plain"]),
            _ => throw new InvalidDataException("Unknown installed reader.")}).ToArray();
    }

    internal ReaderInfo[] Select(string media, string? preferred)
    {
        var candidates = Installed().Where(r => r.MediaTypes.Contains(media) && (preferred is null || r.Id == preferred)).ToArray();
        if (candidates.Length == 0)
        {
            throw new InvalidDataException("No installed compatible reader for this request.");
        }

        return candidates;
    }

    internal static string MediaType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".md" => "text/markdown",
        ".txt" => "text/plain",
        _ => throw new InvalidDataException("Unsupported file type; use UTF-8 .md or .txt.")};
}
