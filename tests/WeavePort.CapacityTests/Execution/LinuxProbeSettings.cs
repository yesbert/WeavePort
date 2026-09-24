namespace WeavePort.CapacityTests;

internal sealed record LinuxProbeSettings(string Languages, int[] Counts, int Seconds, string[] Workloads)
{
    internal static LinuxProbeSettings ReadEnvironment()
    {
        string languages = Environment.GetEnvironmentVariable("WEAVEPORT_LANGUAGES") ?? "mixed";
        if (languages is not ("mixed" or "csharp" or "python" or "typescript" or "diagnostic-python"))
        {
            throw new ArgumentException("WEAVEPORT_LANGUAGES");
        }

        int[] counts = (Environment.GetEnvironmentVariable("WEAVEPORT_COUNTS") ?? "1,64,128").Split(',').Select(int.Parse).ToArray();
        if (counts.Length == 0 || counts[0] < 1 || counts[^1] > 384 || !counts.SequenceEqual(counts.Distinct().Order()))
        {
            throw new ArgumentException("WEAVEPORT_COUNTS");
        }

        int seconds = RequestDiagnostics.ReadInt("WEAVEPORT_SECONDS", 20, 2, 120);
        string[] workloads = (Environment.GetEnvironmentVariable("WEAVEPORT_WORKLOADS") ?? "echo,payload").Split(',');
        if (workloads.Any(w => w is not ("echo" or "payload" or "delay")))
        {
            throw new ArgumentException("WEAVEPORT_WORKLOADS");
        }

        return new(languages, counts, seconds, workloads);
    }
}
