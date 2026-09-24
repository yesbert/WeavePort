namespace WeavePort.Benchmarks;

internal sealed class Lane
{
    internal const int Buckets = 4096;
    // Logarithmic buckets bound storage while retaining approximately 1% resolution.
    private const double BucketGrowth = 1.01;
    private const double NanosecondsPerMillisecond = 1_000_000;

    internal void AddTo(long[] destination)
    {
        for (int index = 0; index < Histogram.Length; index++)
        {
            destination[index] += Histogram[index];
        }
    }

    public long Count { get; private set; }
    public long Errors { get; set; }
    public string? FirstError { get; set; }
    public long[] Histogram { get; } = new long[Buckets];
    internal void Record(double nanoseconds)
    {
        int bucket = Math.Clamp((int)Math.Ceiling(Math.Log(Math.Max(1, nanoseconds), BucketGrowth)), 0, Buckets - 1);
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
            if (count >= target)
            {
                return Math.Pow(BucketGrowth, i) / NanosecondsPerMillisecond;
            }
        }
        return double.NaN;
    }
}
