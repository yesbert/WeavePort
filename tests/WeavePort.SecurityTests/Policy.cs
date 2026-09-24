using System.Text.Json;
using System.Text.Json.Nodes;

internal static class Policy
{
    internal static void Validate(JsonElement inspect)
    {
        JsonElement host = inspect.GetProperty("HostConfig");
        Require(inspect.GetProperty("Config").GetProperty("User").GetString() == "65532:65532", "non-root user");
        Require(!host.GetProperty("Privileged").GetBoolean(), "unprivileged container");
        Require(host.GetProperty("ReadonlyRootfs").GetBoolean(), "read-only root filesystem");
        Require(host.GetProperty("NetworkMode").GetString() == "none", "network isolation");
        Require(host.GetProperty("PidMode").GetString() != "host", "PID isolation");
        Require(host.GetProperty("IpcMode").GetString() != "host", "IPC isolation");
        Require(Has(host, "CapDrop", "ALL"), "dropped capabilities");
        Require(Has(host, "SecurityOpt", "no-new-privileges"), "privilege escalation prevention");
        Require(!Has(host, "SecurityOpt", "seccomp=unconfined"), "seccomp confinement");
        Require(host.GetProperty("Memory").GetInt64() == 256L * 1024 * 1024, "memory limit");
        Require(host.GetProperty("MemorySwap").GetInt64() == 256L * 1024 * 1024, "swap limit");
        Require(host.GetProperty("NanoCpus").GetInt64() == 500000000, "CPU limit");
        Require(host.GetProperty("PidsLimit").GetInt64() == 64, "PID limit");
        Require(host.GetProperty("LogConfig").GetProperty("Type").GetString() == "none", "disabled daemon logs");
        Require(host.GetProperty("Tmpfs").GetProperty("/tmp").GetString()!.Contains("size=16m", StringComparison.Ordinal), "tmpfs limit");
        Require(inspect.GetProperty("Mounts").GetArrayLength() == 0, "no host mounts");
    }

    private static bool Has(JsonElement host, string name, string value) =>
        host.GetProperty(name).EnumerateArray().Any(item => item.GetString() == value);

    private static void Require(bool condition, string control)
    {
        if (!condition)
        {
            throw new InvalidDataException("Pressure-test policy gate rejected configuration: " + control);
        }
    }

    internal static int NegativeControls(JsonElement original)
    {
        Action<JsonNode>[] mutations =
        [
            n => n["HostConfig"]!["Memory"] = 0,
            n => n["HostConfig"]!["MemorySwap"] = -1,
            n => n["HostConfig"]!["NanoCpus"] = 0,
            n => n["HostConfig"]!["PidsLimit"] = 0,
            n => n["HostConfig"]!["Privileged"] = true,
            n => n["HostConfig"]!["ReadonlyRootfs"] = false,
            n => n["HostConfig"]!["NetworkMode"] = "host",
            n => n["HostConfig"]!["PidMode"] = "host",
            n => n["HostConfig"]!["IpcMode"] = "host",
            n => n["Config"]!["User"] = "0",
            n => n["HostConfig"]!["CapDrop"] = new JsonArray(),
            n => n["HostConfig"]!["SecurityOpt"] = new JsonArray(),
            n => n["HostConfig"]!["SecurityOpt"] = new JsonArray("no-new-privileges", "seccomp=unconfined"),
            n => n["Mounts"] = new JsonArray(new JsonObject { ["Destination"] = "/host" }),
            n => n["HostConfig"]!["Tmpfs"]!["/tmp"] = "rw,size=1g",
            n => n["HostConfig"]!["LogConfig"]!["Type"] = "json-file"
        ];
        foreach (var mutate in mutations)
        {
            JsonNode n = JsonNode.Parse(original.GetRawText())!;
            mutate(n);
            try
            {
                Validate(JsonSerializer.SerializeToElement(n));
            }
            catch (InvalidDataException) { continue; }
            throw new InvalidDataException("Policy negative control accepted.");
        }
        return mutations.Length;
    }

    internal static async Task<JsonElement> InspectAsync(string instance) => JsonElement.Parse(await ResourceChecks.DockerAsync(["inspect", instance]))[0];
    internal static async Task<JsonElement> CgroupAsync(string instance)
    {
        const string script = "import json,pathlib; p=pathlib.Path('/sys/fs/cgroup'); print(json.dumps({n:(p/n).read_text() for n in ['memory.max','memory.swap.max','memory.current','memory.events','pids.max','pids.current','pids.events','cpu.max','cpu.stat']}))";
        return JsonElement.Parse(await ResourceChecks.DockerAsync(["exec", "--user", "65532:65532", instance, "python", "-c", script]));
    }
}
