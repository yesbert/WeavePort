using System.Text.Json;

internal static class SecurityObservations
{
    internal static async Task RunAsync(Evidence evidence, string language, string instance)
    {
        await evidence.Check(language + ": effective sandbox identity and syscall filter", async () =>
        {
            JsonElement inspect = JsonElement.Parse(await ResourceChecks.DockerAsync(["inspect", instance]))[0];
            JsonElement config = inspect.GetProperty("HostConfig");
            string status = await ResourceChecks.DockerAsync(["exec", "--user", "65532:65532", instance, "/bin/sh", "-c", "cat /proc/1/status"]);
            var fields = status.Split('\n').Where(line => line.Contains(':')).Select(line => line.Split(':', 2))
                .Where(pair => new[] { "Uid", "Gid", "CapInh", "CapPrm", "CapEff", "CapBnd", "CapAmb", "NoNewPrivs", "Seccomp", "Seccomp_filters" }.Contains(pair[0]))
                .ToDictionary(pair => pair[0], pair => pair[1].Trim());
            string uidMap = await ResourceChecks.DockerAsync(["exec", instance, "cat", "/proc/1/uid_map"]);
            string lsm = await ResourceChecks.DockerAsync(["exec", instance, "/bin/sh", "-c", "cat /proc/1/attr/current 2>/dev/null || printf unavailable"]);
            var observation = new
            {
                language, image = inspect.GetProperty("Image").GetString(), fields, uidMap, lsm,
                configured = new
                {
                    user = inspect.GetProperty("Config").GetProperty("User").GetString(),
                    privileged = config.GetProperty("Privileged"), network = config.GetProperty("NetworkMode"),
                    pidMode = config.GetProperty("PidMode"), ipcMode = config.GetProperty("IpcMode"),
                    readOnlyRoot = config.GetProperty("ReadonlyRootfs"), capabilities = config.GetProperty("CapDrop"),
                    securityOptions = config.GetProperty("SecurityOpt"), runtime = config.GetProperty("Runtime"),
                    mounts = inspect.GetProperty("Mounts").EnumerateArray().Select(m => new { destination = m.GetProperty("Destination"), writable = m.GetProperty("RW") }).ToArray()
                },
                scope = "Trusted Docker observation of controlled fixture PID 1; not remote attestation or kernel-exploit resistance."
            };
            await File.WriteAllTextAsync(Path.Combine(evidence.DirectoryPath, "security-" + language + ".json"), JsonSerializer.Serialize(observation, new JsonSerializerOptions { WriteIndented = true }));
            if (fields["Seccomp"] != "2" || fields["NoNewPrivs"] != "1" ||
                new[] { "CapInh", "CapPrm", "CapEff", "CapBnd", "CapAmb" }.Any(key => Convert.ToUInt64(fields[key], 16) != 0) ||
                fields["Uid"].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Any(id => id != "65532") ||
                fields["Gid"].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Any(id => id != "65532") ||
                config.GetProperty("Privileged").GetBoolean() || !config.GetProperty("ReadonlyRootfs").GetBoolean() ||
                config.GetProperty("NetworkMode").GetString() != "none" || config.GetProperty("PidMode").GetString() == "host" ||
                config.GetProperty("IpcMode").GetString() == "host")
                throw new Exception("Effective sandbox baseline missing; see retained observation");
            JsonElement[] mounts = inspect.GetProperty("Mounts").EnumerateArray().ToArray();
            if (TestProfiles.SocketTransport is null ? mounts.Length != 0 :
                mounts.Length != 1 || mounts[0].GetProperty("Destination").GetString() != "/run/weaveport" || mounts[0].GetProperty("RW").GetBoolean())
                throw new Exception("Unexpected worker mount exposure");
        });
    }
}
