using System.Diagnostics;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;

// Records, without asserting a target, how function files perform per language and block size on the local host.
internal static class FileMeasurements
{
    private const int Repetitions = 5;
    private static readonly string[] Languages = ["csharp", "python", "typescript"];
    private static readonly int[] BlockSizes = [65536, 262144];
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new { });

    internal static async Task RunAsync(string[] args)
    {
        string root = Path.GetFullPath(args.ElementAtOrDefault(1) ?? Environment.CurrentDirectory);
        var results = new List<object>();
        var cases = from language in Languages
                    from blockBytes in BlockSizes
                    select (language, blockBytes);
        foreach (var (language, blockBytes) in cases)
        {
            results.Add(await MeasureAsync(root, language, blockBytes));
        }

        string report = JsonSerializer.Serialize(new
        {
            inputBytes = FileChecks.Archive.Length,
            repetitions = Repetitions,
            machine = Environment.MachineName.Length > 0 ? RuntimeInformationSummary() : "",
            results
        }, Indented);
        string? output = args.FirstOrDefault(argument => argument.StartsWith("--output=", StringComparison.Ordinal))?["--output=".Length..];
        if (output is not null)
        {
            await File.WriteAllTextAsync(output, report);
        }

        Console.WriteLine(report);
    }

    private static async Task<object> MeasureAsync(string root, string language, int blockBytes)
    {
        await using var host = new PluginHost();
        IPluginSession session = await host.BindAsync(new PluginContext("measure-" + language, "files", "1", "test", Empty), FileChecks.Profile(root, language, blockBytes), new FileChecks.NoCallbacks(), []);
        await using var client = new LocalPluginClient(session);
        int pid = (await client.CallAsync("stats", Empty)).GetProperty("pid").GetInt32();
        await client.CallWithFilesAsync("copy", Empty, Files(new FileChecks.MemoryDestination(), out _));
        var copies = new List<double>();
        var summaries = new List<double>();
        int copyReads = 0;
        int summaryReads = 0;
        await using var memory = new MemorySampler(pid);
        for (int run = 0; run < Repetitions; run++)
        {
            long started = Stopwatch.GetTimestamp();
            await client.CallWithFilesAsync("copy", Empty, Files(new FileChecks.MemoryDestination(), out FileChecks.CountingReader copyReader));
            copies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            copyReads = copyReader.Reads;
            started = Stopwatch.GetTimestamp();
            await client.CallWithFilesAsync("zip-summary", Empty, Files(null, out FileChecks.CountingReader summaryReader));
            summaries.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            summaryReads = summaryReader.Reads;
        }

        double copyMedian = Median(copies);
        int writes = (int)Math.Ceiling(FileChecks.Archive.Length / (double)blockBytes);
        return new
        {
            language,
            blockBytes,
            copyMedianMs = Math.Round(copyMedian, 1),
            copyMiBPerSecond = Math.Round(FileChecks.Archive.Length / 1048576.0 / (copyMedian / 1000), 1),
            copyHostReads = copyReads,
            copyOutputBlocks = writes,
            copyMsPerBlock = Math.Round(copyMedian / (copyReads + writes), 3),
            zipSummaryMedianMs = Math.Round(Median(summaries), 1),
            zipSummaryHostReads = summaryReads,
            workerInitialMiB = Math.Round(memory.WorkerInitialBytes / 1048576.0, 1),
            workerPeakMiB = Math.Round(memory.WorkerPeakBytes / 1048576.0, 1)
        };
    }

    private static PluginFiles Files(FileChecks.MemoryDestination? destination, out FileChecks.CountingReader reader)
    {
        reader = new FileChecks.CountingReader(PluginInputReader.FromStream(new MemoryStream(FileChecks.Archive, writable: false)));
        var input = new PluginInput("sheet", FileChecks.Archive.Length, "application/zip", reader);
        return destination is null ? new PluginFiles([input]) : new PluginFiles([input], destination, new PluginFileLimits(1, 16 << 20));
    }

    private static double Median(List<double> values)
    {
        double[] sorted = [.. values.Order()];
        return sorted[sorted.Length / 2];
    }

    private static string RuntimeInformationSummary() => $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription}; {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}; {Environment.ProcessorCount} cores; .NET {Environment.Version}";
}
