using BenchmarkDotNet.Running;
using WeavePort.Benchmarks;

if (args.Length is 6 or 7 && args[0] == "load")
{
    await Load.MeasureAsync(args[1], args[2] == "gateway", args[3], int.Parse(args[4]), int.Parse(args[5]), args.Length == 7 ? int.Parse(args[6]) : 3);
    return;
}

var summaries = BenchmarkSwitcher.FromAssembly(typeof(SdkBenchmarks).Assembly).Run(args).ToArray();
if (summaries.Length == 0 || summaries.Any(s => s.HasCriticalValidationErrors || s.Reports.Any(r => !r.Success)))
{
    Environment.ExitCode = 1;
}
