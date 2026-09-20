using System.Text.Json;

internal static class HistogramChecks
{
    internal static void Run()
    {
        var first = new Histogram();
        var second = new Histogram();
        var values = Enumerable.Range(1, 1000).Select(i => i / 7.0).ToArray();
        foreach (double value in values.Take(500)) first.Add(value);
        foreach (double value in values.Skip(500)) second.Add(value);
        first.Merge(second);
        foreach (double q in new[] { .5, .95, .99 })
        {
            double expected = values[(int)Math.Ceiling(values.Length * q) - 1];
            double actual = first.Quantile(q);
            if (actual < expected || actual > expected * 1.010001) throw new InvalidOperationException("Incorrect merged percentile");
        }
        var report = JsonSerializer.SerializeToElement(first.Report());
        if (report.GetProperty("count").GetInt64() != 1000 || Math.Abs(report.GetProperty("mean").GetDouble() - values.Average()) > .000001)
            throw new InvalidOperationException("Incorrect histogram count/mean");
        if (new Histogram().Quantile(.99) != 0) throw new InvalidOperationException("Incorrect empty percentile");
        long began = GC.GetAllocatedBytesForCurrentThread();
        var sparse = Enumerable.Range(0, 1000).Select(_ => new DensityStats()).ToArray();
        foreach (var client in sparse) { client.Latency.Add(1); client.Execution.Add(1); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - began;
        GC.KeepAlive(sparse);
        if (allocated > 4 * 1048576) throw new InvalidOperationException("Sparse customer recorder allocates excessive memory");
        Console.WriteLine(JsonSerializer.Serialize(new { passed = true, allocatedBytesFor1000TwoSampleCustomers = allocated }));
    }
}
