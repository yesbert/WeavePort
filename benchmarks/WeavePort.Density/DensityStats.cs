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
    internal object Report() => new
    {
        Success,
        Failed,
        Dropped,
        Cold,
        Statuses,
        latency = Latency.Report(),
        coldLatency = ColdLatency.Report(),
        residentLatency = ResidentLatency.Report(),
        queue = Queue.Report(),
        execution = Execution.Report(),
        arrivalLag = ArrivalLag.Report()
    };
    internal static DensityStats Combine(IEnumerable<DensityStats> lanes)
    {
        var result = new DensityStats();
        foreach (var lane in lanes)
        {
            result.Merge(lane);
        }
        return result;
    }
    private void Merge(DensityStats other)
    {
        Success += other.Success;
        Failed += other.Failed;
        Dropped += other.Dropped;
        Cold += other.Cold;
        ColdLatency.Merge(other.ColdLatency);
        ResidentLatency.Merge(other.ResidentLatency);
        Latency.Merge(other.Latency);
        Queue.Merge(other.Queue);
        Execution.Merge(other.Execution);
        ArrivalLag.Merge(other.ArrivalLag);
        foreach (var status in other.Statuses)
        {
            Statuses[status.Key] = Statuses.GetValueOrDefault(status.Key) + status.Value;
        }
    }

}
