using WeavePort.Hosting;
using System.Text.Json;
using DocumentWorkshop.Contracts;
using WeavePort.Abstractions;

namespace DocumentWorkshop.Host;

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
            _ => throw new InvalidDataException("Unknown installed reader.")
        }).ToArray();
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
        _ => throw new InvalidDataException("Unsupported file type; use UTF-8 .md or .txt.")
    };
}
