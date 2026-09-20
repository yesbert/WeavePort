internal sealed class DensityStats
{
    internal long Success, Failed, Dropped, Cold;
    internal long Errors => Failed + Dropped;
    internal Dictionary<string, long> Statuses { get; } = [];
    internal Histogram Latency { get; } = new();
    internal Histogram ColdLatency { get; } = new();
    internal Histogram ResidentLatency { get; } = new();
    internal Histogram Queue { get; } = new();
    internal Histogram Execution { get; } = new();
    internal Histogram ArrivalLag { get; } = new();
    internal object Report() => new { Success, Failed, Dropped, Cold, Statuses,
        latency = Latency.Report(), coldLatency = ColdLatency.Report(), residentLatency = ResidentLatency.Report(), queue = Queue.Report(), execution = Execution.Report(), arrivalLag = ArrivalLag.Report() };
    internal static DensityStats Combine(IEnumerable<DensityStats> lanes)
    {
        var result = new DensityStats();
        foreach (var lane in lanes)
        {
            result.Success += lane.Success; result.Failed += lane.Failed; result.Dropped += lane.Dropped; result.Cold += lane.Cold;
            result.ColdLatency.Merge(lane.ColdLatency); result.ResidentLatency.Merge(lane.ResidentLatency);
            result.Latency.Merge(lane.Latency); result.Queue.Merge(lane.Queue); result.Execution.Merge(lane.Execution); result.ArrivalLag.Merge(lane.ArrivalLag);
            foreach (var status in lane.Statuses) result.Statuses[status.Key] = result.Statuses.GetValueOrDefault(status.Key) + status.Value;
        }
        return result;
    }
}

// Bounded logarithmic histograms: ~1% buckets, milliseconds; maxima remain exact.
internal sealed class Histogram
{
    private readonly SortedDictionary<int, long> _buckets = [];
    private long _count;
    private double _sum, _maximum;
    internal void Add(double ms)
    {
        ms = Math.Max(0, ms);
        int bucket = ms <= .001 ? 0 : Math.Min(2399, (int)Math.Ceiling(Math.Log(ms / .001) / Math.Log(1.01)));
        _buckets[bucket] = _buckets.GetValueOrDefault(bucket) + 1; _count++; _sum += ms; _maximum = Math.Max(_maximum, ms);
    }
    internal void Merge(Histogram other)
    {
        foreach (var entry in other._buckets) _buckets[entry.Key] = _buckets.GetValueOrDefault(entry.Key) + entry.Value;
        _count += other._count; _sum += other._sum; _maximum = Math.Max(_maximum, other._maximum);
    }
    internal double Quantile(double quantile)
    {
        if (_count == 0) return 0;
        long seen = 0, target = (long)Math.Ceiling(_count * quantile);
        foreach (var entry in _buckets) { seen += entry.Value; if (seen >= target) return .001 * Math.Pow(1.01, entry.Key); }
        return _maximum;
    }
    internal object Report() => new { count = _count, mean = _count == 0 ? 0 : _sum / _count, p50 = Quantile(.5), p95 = Quantile(.95), p99 = Quantile(.99), max = _maximum };
}
