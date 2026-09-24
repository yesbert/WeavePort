using System.Text.Json;

if (args.SequenceEqual(["--observer-check"]))
{
    ObserverChecks.Run();
    return;
}
if (args.SequenceEqual(["--histogram-check"])) { HistogramChecks.Run(); return; }
if (args.Length != 1)
{
    throw new ArgumentException("Pass one JSON configuration file.");
}

var config = JsonSerializer.Deserialize<DensityConfig>(await File.ReadAllTextAsync(args[0]))!;
await DensityRun.ExecuteAsync(config);
