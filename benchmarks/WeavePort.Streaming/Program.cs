using System.Text.Json;

if (args.Length != 5)
{
    throw new ArgumentException("Expected source directory, fixture directory, C# worker, label and output path.");
}

string source = Path.GetFullPath(args[0]);
string fixtures = Path.GetFullPath(args[1]);
string worker = Path.GetFullPath(args[2]);
string label = args[3];
string output = Path.GetFullPath(args[4]);
var rows = new List<object>();
string[] languages = Environment.GetEnvironmentVariable("WP_STREAM_LANGUAGE") is { Length: > 0 } selected ? [selected] : ["csharp", "python", "typescript"];
var cases = from language in languages
            from remote in new[] { false, true }
            select (language, remote);
foreach (var (language, remote) in cases)
{
    rows.AddRange(await StreamingMeasurement.RunAsync(source, fixtures, worker, label, language, remote));
}

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { label, source, sdkRuntime = Environment.Version.ToString(), os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, cores = Environment.ProcessorCount, serverGc = System.Runtime.GCSettings.IsServerGC, sourceBuilt = true, gatewaySameProcess = true, validation = "existing SDK Workload.Check over every result", rows }, new JsonSerializerOptions { WriteIndented = true }));
