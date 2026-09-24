internal sealed record LocalCapacitySettings(int[] Counts, int[] PayloadSizes, int MaximumSummedRssGiB,
    int MaximumHostRssGiB, int ObservationBudget, int Seconds)
{
    internal static LocalCapacitySettings ReadEnvironment()
    {
        int[] counts = (Environment.GetEnvironmentVariable("WEAVEPORT_LOCAL_COUNTS") ?? "1,64,128,256,384").Split(',').Select(int.Parse).ToArray();
        int[] payloadSizes = (Environment.GetEnvironmentVariable("WEAVEPORT_LOCAL_PAYLOAD_CHARS") ?? "16,65536").Split(',').Select(int.Parse).ToArray();
        if (payloadSizes.Length == 0 || payloadSizes.Any(n => n is not (16 or 65536)) || !payloadSizes.SequenceEqual(payloadSizes.Distinct().Order()))
        {
            throw new ArgumentException("Payloads must be ascending unique values from 16,65536");
        }

        int maxSummedRssGiB = int.Parse(Environment.GetEnvironmentVariable("WEAVEPORT_LOCAL_MAX_RSS_GIB") ?? "12");
        if (maxSummedRssGiB != 0 && maxSummedRssGiB is < 12 or > 32)
        {
            throw new ArgumentException("RSS exploration ceiling must be 0 (disabled) or 12..32 GiB");
        }

        int maxHostRssGiB = int.Parse(Environment.GetEnvironmentVariable("WEAVEPORT_LOCAL_MAX_HOST_RSS_GIB") ?? "2");
        if (maxHostRssGiB is < 0 or > 32)
        {
            throw new ArgumentException("Host RSS ceiling must be 0 (disabled) or 1..32 GiB");
        }

        int observationBudget = int.Parse(Environment.GetEnvironmentVariable("WEAVEPORT_LOCAL_OBSERVATION_BUDGET") ?? "8000000");
        if (observationBudget is < 8000000 or > 32000000)
        {
            throw new ArgumentException("Observation budget must be 8000000..32000000");
        }

        int seconds = int.Parse(Environment.GetEnvironmentVariable("WEAVEPORT_LOCAL_SECONDS") ?? "20");
        if (seconds is < 2 or > 60 || counts.Length == 0 || counts.Any(n => n is < 1 or > 4096) || !counts.SequenceEqual(counts.Distinct().Order()))
        {
            throw new ArgumentException("Invalid bounded local load settings");
        }

        return new(counts, payloadSizes, maxSummedRssGiB, maxHostRssGiB, observationBudget, seconds);
    }
}
