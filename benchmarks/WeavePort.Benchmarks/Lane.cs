namespace WeavePort.Benchmarks;

internal sealed class Lane
{
    internal const int Buckets = 4096;
    public long Count { get; private set; }
    public long Errors { get; set; }
    public string? FirstError { get; set; }
    public long[] Histogram { get; } = new long[Buckets];
    internal void Record(double nanoseconds)
    {
        int bucket = Math.Clamp((int)Math.Ceiling(Math.Log(Math.Max(1, nanoseconds), 1.01)), 0, Buckets - 1);
        Histogram[bucket]++;
        Count++;
    }
    internal static double Quantile(long[] histogram, double quantile)
    {
        long target = (long)Math.Ceiling(histogram.Sum() * quantile);
        long count = 0;
        for (int i = 0; i < histogram.Length; i++)
        {
            count += histogram[i];
            if (count >= target) return Math.Pow(1.01, i) / 1_000_000;
        }
        return double.NaN;
    }
}
