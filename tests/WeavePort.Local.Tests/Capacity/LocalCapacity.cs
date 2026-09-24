using WeavePort.LocalTools;

internal static class LocalCapacity
{
    internal static async Task<int> RunAsync(LocalConfiguration config, string output, LinuxComparison? comparison = null)
    {
        if (await LocalVerification.DoctorAsync(config, output) != 0)
        {
            return 1;
        }
        double? initialSwapMiB = await CapacityDiagnostics.SwapMiBAsync();
        var settings = LocalCapacitySettings.ReadEnvironment();
        return await new LocalCapacityRun(config, output, comparison, settings, initialSwapMiB).RunAsync();
    }
}
