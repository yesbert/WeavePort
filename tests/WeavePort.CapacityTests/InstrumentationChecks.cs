namespace WeavePort.CapacityTests;

internal static class InstrumentationChecks
{
    internal static async Task RunRestartAsync()
    {
        TimeProvider clock = TimeProvider.System;
        await using var observer = new VmObserver(clock);
        await using var host = new WeavePort.Hosting.PluginHost();
        await observer.StartAsync();
        await using var telemetry = new Telemetry(observer, clock);
        var experiment = new LoadExperiment(host, clock, telemetry, CancellationToken.None);
        telemetry.Start();
        await experiment.GrowAsync(1, "csharp");
        await telemetry.WatchAsync(experiment.WorkerNames);
        string previous = experiment.Tenants[0].Session.Instance;
        Task<LoadResult> load = experiment.RunAsync("echo", 15);
        await Task.Delay(2000);
        await Commands.RunAsync("docker", "kill", previous);
        LoadResult result = await load;
        if (result.Errors == 0 || result.Errors > 2 || experiment.Tenants[0].Session.Instance == previous || telemetry.StopReason is not null)
            throw new InvalidOperationException("Restart telemetry did not follow the owned worker: " + telemetry.StopReason);
        await Task.Delay(2000);
        ResourceSample last = telemetry.Samples.Last();
        if (last.MissingWorkers != 0 || last.Workers.Length != 1 || last.Workers[0].Name != experiment.Tenants[0].Session.Instance)
            throw new InvalidOperationException("Replacement worker not measured");
        await telemetry.WatchAsync(() => []);
        await experiment.ShrinkAsync(0);
        Console.WriteLine("PASS instrumentation: forced worker exit, automatic restart, live telemetry replacement and owned cleanup");
    }

    internal static void Run()
    {
        TimeProvider clock = TimeProvider.System;
        string[] unavailable =
        [
            "\u001b[2J\u001b[H",
            """{"Name":"worker","MemUsage":"-- / --","CPUPerc":"--","PIDs":"--"}""",
            """{"Name":"worker","MemUsage":"--","CPUPerc":"0.00%","PIDs":"1"}"""
        ];
        foreach (string sample in unavailable)
            if (Telemetry.ParseSample(sample, clock) is not null) throw new InvalidOperationException("Unavailable telemetry must not become zero usage");
        WorkerSample valid = Telemetry.ParseSample("\u001b[2J\u001b[H" + """{"Name":"worker","MemUsage":"12.5MiB / 256MiB","CPUPerc":"125.50%","PIDs":"3"}""", clock)
            ?? throw new InvalidOperationException("Valid telemetry missing");
        if (valid.MemoryBytes != 12.5 * 1024 * 1024 || valid.CpuPercent != 125.5 || valid.Pids != 3)
            throw new InvalidOperationException("Telemetry unit conversion failed");
        bool rejected = false;
        try { Telemetry.ParseSample("{broken", clock); }
        catch (System.Text.Json.JsonException) { rejected = true; }
        if (!rejected) throw new InvalidOperationException("Malformed telemetry must be reported");
        TenantRequests[] distribution =
        [
            new(0, "csharp", new double[1000], new Dictionary<string, int> { ["ok"] = 1000 }),
            new(1, "csharp", [1500, 3000], new Dictionary<string, int> { ["ok"] = 2 })
        ];
        if (WeavePort.Testing.ContractChecks.Percentile(distribution.SelectMany(r => r.LatenciesMs), 99) != 0 ||
            LoadExperiment.WorstTenantP99(distribution) != 3000)
            throw new InvalidOperationException("Aggregate percentiles must not conceal the slow customer");
        var quality = new LoadResult(2, "payload", 1, 1002, 0, 1002, 0, 0, 0, 3000, 2, 1000,
            distribution, 1, 1002, 1002, 0, 0, 0, 3000);
        if (!quality.ExceedsQualityBoundary || (quality with { WorstTenantP99Ms = 0 }).ExceedsQualityBoundary ||
            !(quality with { WorstTenantP99Ms = 0, UnservedTenants = 1 }).ExceedsQualityBoundary)
            throw new InvalidOperationException("Customer quality boundary was not enforced");
        Console.WriteLine("PASS instrumentation: Docker placeholders, terminal escapes, units and malformed data");
        Console.WriteLine("PASS instrumentation: worst-customer tail remains visible when aggregate p99 hides it");
    }
}
