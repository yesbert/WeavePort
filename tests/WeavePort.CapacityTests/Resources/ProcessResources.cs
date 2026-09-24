using System.Globalization;

namespace WeavePort.CapacityTests;

internal sealed class ProcessResources
{
    internal long ChildrenBytes { get; private set; }
    internal int ChildCount { get; private set; }
    internal long VmRssBytes { get; private set; }
    internal double VmCpuPercent { get; private set; }

    internal static async Task<ProcessResources> ReadAsync()
    {
        var result = new ProcessResources();
        string processes = await Commands.RunAsync("ps", "-axo", "pid=,ppid=,rss=,%cpu=,comm=");
        foreach (string line in processes.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            result.Observe(line);
        }
        return result;
    }

    private void Observe(string line)
    {
        string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 5)
        {
            return;
        }
        if (int.Parse(fields[1], CultureInfo.InvariantCulture) == Environment.ProcessId)
        {
            ChildrenBytes += long.Parse(fields[2], CultureInfo.InvariantCulture) * 1024;
            ChildCount++;
        }
        if (line.Contains("com.apple.Virtualization.VirtualMachine", StringComparison.Ordinal))
        {
            VmRssBytes += long.Parse(fields[2], CultureInfo.InvariantCulture) * 1024;
            VmCpuPercent += double.Parse(fields[3], CultureInfo.InvariantCulture);
        }
    }
}
