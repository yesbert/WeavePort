using System.Text.Json;
using System.Text.Json.Nodes;

internal static class Policy
{
    internal static void Validate(JsonElement inspect)
    {
        JsonElement c = inspect.GetProperty("HostConfig");
        bool Has(string name, string value) => c.GetProperty(name).EnumerateArray().Any(x => x.GetString() == value);
        if (inspect.GetProperty("Config").GetProperty("User").GetString() != "65532:65532" ||
            c.GetProperty("Privileged").GetBoolean() || !c.GetProperty("ReadonlyRootfs").GetBoolean() ||
            c.GetProperty("NetworkMode").GetString() != "none" || c.GetProperty("PidMode").GetString() == "host" ||
            c.GetProperty("IpcMode").GetString() == "host" || !Has("CapDrop", "ALL") ||
            !Has("SecurityOpt", "no-new-privileges") || Has("SecurityOpt", "seccomp=unconfined") ||
            c.GetProperty("Memory").GetInt64() != 256L * 1024 * 1024 || c.GetProperty("MemorySwap").GetInt64() != 256L * 1024 * 1024 ||
            c.GetProperty("NanoCpus").GetInt64() != 500000000 || c.GetProperty("PidsLimit").GetInt64() != 64 ||
            c.GetProperty("LogConfig").GetProperty("Type").GetString() != "none" ||
            !c.GetProperty("Tmpfs").GetProperty("/tmp").GetString()!.Contains("size=16m", StringComparison.Ordinal) ||
            inspect.GetProperty("Mounts").GetArrayLength() != 0)
            throw new InvalidDataException("Pressure-test policy gate rejected configuration.");
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
            try { Validate(JsonSerializer.SerializeToElement(n)); }
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
