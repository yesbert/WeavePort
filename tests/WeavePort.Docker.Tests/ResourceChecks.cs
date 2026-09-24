using System.Diagnostics;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;

internal static class ResourceChecks
{
    internal static async Task RunAsync(PluginHost host, DemoCallbacks callbacks, Evidence evidence, Dictionary<string, IPluginSession> sessions)
    {
        foreach ((string language, IPluginSession session) in sessions)
        {
            await SecurityObservations.RunAsync(evidence, language, session.Instance);
            string statistics = await DockerAsync(["stats", "--no-stream", "--format", "{{json .}}", session.Instance]);
            await File.WriteAllTextAsync(Path.Combine(evidence.DirectoryPath, language + "-idle-stats.json"), statistics);
            string inspect = await DockerAsync(["inspect", "--format", "{{json .HostConfig}}", session.Instance]);
            await File.WriteAllTextAsync(Path.Combine(evidence.DirectoryPath, language + "-limits.json"), inspect);
            await evidence.CheckAsync(language + ": enforced container resource profile", () =>
            {
                using JsonDocument limits = JsonDocument.Parse(inspect);
                JsonElement value = limits.RootElement;
                if (value.GetProperty("Memory").GetInt64() != 256L * 1024 * 1024 ||
                    value.GetProperty("MemorySwap").GetInt64() != 256L * 1024 * 1024 ||
                    value.GetProperty("NanoCpus").GetInt64() != 500000000 ||
                    value.GetProperty("PidsLimit").GetInt64() != 64 ||
                    !value.GetProperty("ReadonlyRootfs").GetBoolean() ||
                    value.GetProperty("NetworkMode").GetString() != "none" ||
                    !value.GetProperty("Tmpfs").GetProperty("/tmp").GetString()!.Contains("size=16m", StringComparison.Ordinal) ||
                    !value.GetProperty("CapDrop").EnumerateArray().Any(v => v.GetString() == "ALL") ||
                    !value.GetProperty("SecurityOpt").EnumerateArray().Any(v => v.GetString() == "no-new-privileges"))
                {
                    throw new InvalidOperationException("Runtime resource profile differs from experiment design");
                }

                return Task.CompletedTask;
            });
        }
        await evidence.CheckAsync("installed bindings are lazy", async () =>
        {
            await using IPluginSession installed = await host.BindAsync(new PluginContext("lazy", "demo", "1", "default", JsonSerializer.SerializeToElement(new
            {
            })),
                TestProfiles.Create("weaveport-poc-python:1"), callbacks, DemoCallbacks.Grants);
            if (installed.Instance != "")
            {
                throw new InvalidOperationException("Idle binding launched a worker");
            }
        });
    }
    internal static async Task<string> DockerAsync(string[] args)
    {
        var info = new ProcessStartInfo("docker") { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (string argument in args)
        {
            info.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(info)!;
        string output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new IOException("Docker measurement failed.");
        }

        return output;
    }
}
