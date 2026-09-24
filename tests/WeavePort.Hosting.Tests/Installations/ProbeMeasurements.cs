using System.Text;
using System.Diagnostics;
using System.Text.Json;
using WeavePort.Hosting;

internal static class ProbeMeasurements
{
    internal static async Task RunAsync(string[] args)
    {
        var measurements = new List<object>();
        for (int i = 1; i < args.Length; i += 2)
        {
            var samples = await MeasureAsync(args[i + 1], args[i]);
            samples.Sort();
            measurements.Add(new
            {
                ecosystem = args[i],
                count = samples.Count,
                medianMs = samples[samples.Count / 2],
                minMs = samples[0],
                maxMs = samples[^1]
            });
        }
        Console.WriteLine(JsonSerializer.Serialize(measurements));
    }
    private static async Task<List<double>> MeasureAsync(string executable, string ecosystem)
    {
        var samples = new List<double>();
        for (int sample = 0; sample < 25; sample++)
        {
            var watch = Stopwatch.StartNew();
            await RuntimeProbe.RunAsync(executable, ecosystem, default);
            if (sample < 5)
            {
                continue;
            }
            samples.Add(watch.Elapsed.TotalMilliseconds);
        }
        return samples;
    }

}
