using System.Text.Json;
using DocumentWorkshop.Host;

using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(60));
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};
try
{
    string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException("Run scripts/document-workshop.sh; missing " + name);
    var runtime = new RuntimePaths(Required("WP_DOCUMENT_ROOT"), Required("WP_DOCUMENT_DOTNET"));
    if (args.Length == 2 && args[0] == "--activate")
    {
        runtime.Activate(args[1]);
        Console.WriteLine("Activated release " + args[1]);
        return 0;
    }

    if (args.Length == 1 && args[0] == "--verify")
    {
        await Verification.RunAsync(runtime, stop.Token);
        return 0;
    }

    string file = "samples/DocumentWorkshop/fixtures/handbook.md";
    string configFile = "samples/DocumentWorkshop/config.json";
    string store = Path.Combine(runtime.Root, "documents");
    string tenant = "demo", profile = "default";
    string? reader = null;
    bool list = false;
    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--version" when i + 1 < args.Length:
                runtime = runtime with
                {
                    SelectedVersion = args[++i]
                };
                break;
            case "--file" when i + 1 < args.Length:
                file = args[++i];
                break;
            case "--config" when i + 1 < args.Length:
                configFile = args[++i];
                break;
            case "--store" when i + 1 < args.Length:
                store = args[++i];
                break;
            case "--tenant" when i + 1 < args.Length:
                tenant = args[++i];
                break;
            case "--profile" when i + 1 < args.Length:
                profile = args[++i];
                break;
            case "--reader" when i + 1 < args.Length:
                reader = args[++i];
                break;
            case "--list-readers":
                list = true;
                break;
            default:
                throw new ArgumentException("Use --file PATH, --config PATH, --store PATH, --tenant ID, --profile ID, --reader ID, --list-readers or --verify.");
        }
    }

    var config = JsonSerializer.Deserialize<WorkshopConfiguration>(await File.ReadAllTextAsync(configFile, stop.Token), Json.Options) ?? throw new InvalidDataException("Missing configuration.");
    var catalog = new Catalog(config);
    if (list)
    {
        ListReaders(catalog, runtime);
        return 0;
    }

    var result = await new Importer(runtime, catalog, Console.Out).ImportAsync(new ImportOptions(file, store, tenant, profile, reader), stop.Token);
    Console.WriteLine($"Imported with {result.Reader}: {result.Sections} sections, {result.Fragments} fragments, {result.SourceBytes} source bytes.");
    int shown = 0;
    foreach (string line in File.ReadLines(result.Path))
    {
        using var item = JsonDocument.Parse(line);
        if (item.RootElement.GetProperty("kind").GetString() != "fragment")
        {
            continue;
        }

        var fragment = item.RootElement.GetProperty("fragment");
        if (fragment.GetProperty("part").GetInt32() != 0)
        {
            continue;
        }

        Console.WriteLine($"  {fragment.GetProperty("section")}: {fragment.GetProperty("heading").GetString()} [#{fragment.GetProperty("anchor").GetString()}]");
        if (++shown == 8)
        {
            break;
        }
    }

    Console.WriteLine("Document: " + result.Path);
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Import cancelled; no partial document was committed.");
    return 130;
}
catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or InvalidOperationException or JsonException)
{
    Console.Error.WriteLine("Import refused: " + error.Message);
    return 1;
}

static void ListReaders(Catalog catalog, RuntimePaths runtime)
{
    var release = runtime.Resolve().Identity.Version;
    foreach (var installed in catalog.Installed())
    {
        Console.WriteLine($"{installed.Id} v{release}: {string.Join(", ", installed.MediaTypes)}");
    }
}
