// Bounded logarithmic histograms: ~1% buckets, milliseconds; maxima remain exact.
internal sealed class Histogram
{
    private const double MinimumBucketMs = .001;
    private const double BucketGrowth = 1.01;
    private const int MaximumBucket = 2399;
    private readonly SortedDictionary<int, long> _buckets = [];
    private long _count;
    private double _sum, _maximum;
    internal void Add(double ms)
    {
        ms = Math.Max(0, ms);
        int bucket = ms <= MinimumBucketMs ? 0 : Math.Min(MaximumBucket, (int)Math.Ceiling(Math.Log(ms / MinimumBucketMs) / Math.Log(BucketGrowth)));
        _buckets[bucket] = _buckets.GetValueOrDefault(bucket) + 1;
        _count++;
        _sum += ms;
        _maximum = Math.Max(_maximum, ms);
    }
    internal void Merge(Histogram other)
    {
        foreach (var entry in other._buckets)
        {
            _buckets[entry.Key] = _buckets.GetValueOrDefault(entry.Key) + entry.Value;
        }

        _count += other._count;
        _sum += other._sum;
        _maximum = Math.Max(_maximum, other._maximum);
    }
    internal double Quantile(double quantile)
    {
        if (_count == 0)
        {
            return 0;
        }

        long seen = 0, target = (long)Math.Ceiling(_count * quantile);
        foreach (var entry in _buckets)
        {
            seen += entry.Value;
            if (seen >= target)
            {
                return MinimumBucketMs * Math.Pow(BucketGrowth, entry.Key);
            }
        }
        return _maximum;
    }
    internal object Report() => new { count = _count, mean = _count == 0 ? 0 : _sum / _count, p50 = Quantile(.5), p95 = Quantile(.95), p99 = Quantile(.99), max = _maximum };
}
