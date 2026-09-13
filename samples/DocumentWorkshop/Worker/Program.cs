using DocumentWorkshop.Contracts;
using DocumentWorkshop.Worker;
using WeavePort.Sdk;

#if RELEASE_V2
const string release = "2";
#else
const string release = "1";
#endif
string kind = args.Single();
if (kind is not ("markdown" or "plain"))
{
    throw new ArgumentException("Unknown reader.");
}

var info = new ReaderInfo(kind, release, kind == "markdown" ? ["text/markdown"] : ["text/markdown", "text/plain"]);
Extraction? extraction = null;
await new PluginApplication
{
    PluginVersion = release
}.Function<object, ReaderInfo>("reader.describe", (_, _, _) => ValueTask.FromResult(info)).Function<DocumentSource, ReaderInfo>("reader.open", (source, _, _) =>
{
    if (extraction is not null || !info.MediaTypes.Contains(source.MediaType) || source.Length is < 0 or > Limits.SourceBytes)
    {
        throw new ArgumentException("Invalid or duplicate extraction.");
    }

    extraction = new Extraction(source, kind);
    return ValueTask.FromResult(info);
}).Function<PageRequest, ExtractionPage>("reader.next", async (request, context, token) => await (extraction ?? throw new InvalidOperationException("Open first.")).NextAsync(request, context, token)).RunAsync();
