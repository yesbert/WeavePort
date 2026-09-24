using System.Diagnostics;

namespace WeavePort.Benchmarks;

internal sealed record Resources(double Seconds, long HostRss, long GatewayRss, long WorkerRss, double HostCpu, double GatewayCpu, double WorkerCpu)
{
    internal static Resources Capture(int[] ids, int gateway, double seconds)
    {
        using Process host = Process.GetCurrentProcess();
        long workerRss = 0, gatewayRss = 0;
        double workerCpu = 0, gatewayCpu = 0;
        foreach (int id in ids)
        {
            using Process worker = Process.GetProcessById(id);
            workerRss += worker.WorkingSet64;
            workerCpu += worker.TotalProcessorTime.TotalSeconds;
        }
        if (gateway != 0)
        {
            using Process process = Process.GetProcessById(gateway);
            gatewayRss = process.WorkingSet64;
            gatewayCpu = process.TotalProcessorTime.TotalSeconds;
        }
        return new(seconds, host.WorkingSet64, gatewayRss, workerRss, host.TotalProcessorTime.TotalSeconds, gatewayCpu, workerCpu);
    }
}
