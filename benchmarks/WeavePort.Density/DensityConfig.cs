internal sealed record DensityConfig
{
    public string Root { get; init; } = "";
    public string Output { get; init; } = "";
    public string Adapter { get; init; } = "native";
    public string Language { get; init; } = "python";
    public string? DockerImage { get; init; }
    public string Executable { get; init; } = "/usr/bin/python3";
    public string Mode { get; init; } = "scheduled";
    public bool ApprovedSdk { get; init; }
    public string[]? Arguments { get; init; }
    public int Clients { get; init; } = 8;
    public int RegisteredClients { get; init; }
    public int? Workers { get; init; }
    public long MemoryBudgetMiB { get; init; } = 4096;
    public int WorkerMemoryMiB { get; init; } = 64;
    public int ConcurrentStarts { get; init; } = 8;
    public int Seconds { get; init; } = 10;
    public double Rate { get; init; }
    public string Traffic { get; init; } = "saturation";
    public double CallsPerCustomerPerMinute { get; init; } = 1;
    public int Seed { get; init; } = 1729;
    public int PayloadBytes { get; init; } = 64;
    public int WarmupCustomers { get; init; }
    public int Pristine { get; init; }
    public int P99Ms { get; init; } = 1000;
    public int QueueMs { get; init; } = 5000;
    public int MaxHostRssMiB { get; init; } = 1536;
    public int MaxOwnedRssMiB { get; init; } = 4096;
    public int MaxPending { get; init; } = 4096;
    public int IdleMs { get; init; } = 120000;

    internal void Validate()
    {
        if (Clients is < 1 or > 100000 || RegisteredClients is < 0 or > 100000 ||
            RegisteredClients != 0 && RegisteredClients < Clients || Workers is < 1 || MemoryBudgetMiB < 64 || WorkerMemoryMiB < 64 || WorkerMemoryMiB > MemoryBudgetMiB || ConcurrentStarts < 1 || Seconds is < 1 or > 600 ||
            WarmupCustomers < 0 || WarmupCustomers > Clients || Rate < 0 || Rate > 100000 || PayloadBytes is < 0 or > 65536 || Mode is not ("scheduled" or "direct") ||
            Adapter is not ("native" or "docker") || MaxPending is < 1 or > 10000 ||
            Traffic is not ("saturation" or "population") || CallsPerCustomerPerMinute is <= 0 or > 60 ||
            Traffic == "population" && Seconds < 60 / CallsPerCustomerPerMinute)
        {
            throw new ArgumentException("Configuration outside bounded density experiment limits.");
        }

    }
}
