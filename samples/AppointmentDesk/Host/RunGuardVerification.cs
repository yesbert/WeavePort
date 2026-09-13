using WeavePort.Samples;

namespace AppointmentDesk.Host;
internal static class RunGuardVerification
{
    internal static async Task RunAsync(string root)
    {
        string path = Path.Combine(root, "run-guard");
        string generation;
        await using var coordinator = new EmbeddedCoordinator();
        using (var guard = new NativeRunGuard(path))
        {
            generation = guard.Generation;
            byte[] original = File.ReadAllBytes(Path.Combine(path, "run.json"));
            await Verification.RefusedAsync(() =>
            {
                using var other = new NativeRunGuard(path);
                return Task.CompletedTask;
            });
            Verification.Check(original.SequenceEqual(File.ReadAllBytes(Path.Combine(path, "run.json"))), "concurrent run cannot replace exclusive run marker");
            try
            {
                guard.MarkClean(coordinator.Snapshot);
                throw new IOException("Expected unclean refusal.");
            }
            catch (InvalidOperationException)
            {
            }

            Verification.Check(File.Exists(Path.Combine(path, "run.json")), "incomplete shutdown cannot clear run marker");
            Directory.CreateDirectory(guard.WorkspaceRoot);
            guard.MarkClean(await coordinator.StopAsync(TimeSpan.Zero, TimeSpan.FromSeconds(2)));
        }

        Verification.Check(!File.Exists(Path.Combine(path, "run.json")), "confirmed clean shutdown removes its run marker");
        using (var guard = new NativeRunGuard(path))
        {
            Verification.Check(guard.Generation != generation && guard.WorkspaceRoot.EndsWith(guard.Generation, StringComparison.Ordinal), "new run receives a fresh workspace generation");
        }

        Verification.Check(File.Exists(Path.Combine(path, "run.json")), "disposing guard alone retains restart gate");
        await Verification.RefusedAsync(() =>
        {
            using var other = new NativeRunGuard(path);
            return Task.CompletedTask;
        });
        Verification.Check(true, "abandoned marker blocks subsequent execution");
        string invalid = Path.Combine(root, "invalid-run-guard");
        Directory.CreateDirectory(invalid);
        File.WriteAllText(Path.Combine(invalid, "run.json"), "");
        await Verification.RefusedAsync(() =>
        {
            using var guard = new NativeRunGuard(invalid);
            return Task.CompletedTask;
        });
        Verification.Check(new FileInfo(Path.Combine(invalid, "run.json")).Length == 0, "partial marker is refused unchanged");
    }
}
