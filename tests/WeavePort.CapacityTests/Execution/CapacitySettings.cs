namespace WeavePort.CapacityTests;

internal sealed record CapacitySettings(string Root, int[] Counts, int Seconds, string Languages, string[] Workloads)
{
    internal static CapacitySettings Parse(string[] args)
    {
        string root = Path.GetFullPath(args[0]);
        int[] counts = (args.Length > 1 ? args[1] : "1,8,32,64,128,256").Split(',').Select(int.Parse).ToArray();
        int seconds = args.Length > 2 ? int.Parse(args[2]) : 10;
        string languages = args.Length > 3 ? args[3] : "mixed";
        string[] workloads = (args.Length > 4 ? args[4] : "echo,delay,payload").Split(',');
        if (counts.Length == 0 || counts[0] < 1 || counts[^1] > 1024 || !counts.SequenceEqual(counts.Distinct().Order()) || seconds < 2 || seconds > 120 ||
            languages is not ("mixed" or "csharp" or "python" or "typescript" or "diagnostic-python") || workloads.Any(w => w is not ("echo" or "delay" or "payload")))
        {
            throw new ArgumentException("Expected increasing counts 1..1024, duration 2..120 seconds, mixed|csharp|python|typescript|diagnostic-python, and echo,delay,payload workloads");
        }

        return new(root, counts, seconds, languages, workloads);
    }
}
