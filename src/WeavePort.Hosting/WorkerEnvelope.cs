using System.Text.Json;

namespace WeavePort.Hosting;
internal static class WorkerEnvelope
{
    private static readonly string[] ReservedNames = ["type", "id", "callbackId", "operation", "payload", "value", "protocol", "pluginVersion", "code", "sessionCleanup", "reusable", "concurrentCalls", "degree"];
    internal static void Validate(JsonElement frame)
    {
        if (frame.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Expected protocol object.");
        }

        int seen = 0;
        foreach (JsonProperty property in frame.EnumerateObject())
        {
            // NameEquals handles escaped spellings without allocating property-name strings.
            int flag = ReservedField(property);
            if ((seen & flag) != 0)
            {
                throw new InvalidDataException("Repeated reserved protocol field.");
            }

            seen |= flag;
        }
    }

    private static int ReservedField(JsonProperty property)
    {
        for (int index = 0; index < ReservedNames.Length; index++)
        {
            if (property.NameEquals(ReservedNames[index]))
            {
                return 1 << index;
            }
        }

        return 0;
    }

    internal static void ValidateReady(JsonElement frame, string version, WorkerReusePolicy reusePolicy = WorkerReusePolicy.CustomerBound)
    {
        Validate(frame);
        if (!frame.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.String || type.GetString() != "ready" || !frame.TryGetProperty("protocol", out JsonElement protocol) || protocol.ValueKind != JsonValueKind.Number || !protocol.TryGetInt32(out int number) || number != (reusePolicy == WorkerReusePolicy.Shared ? 2 : 1) || !frame.TryGetProperty("pluginVersion", out JsonElement pluginVersion) || pluginVersion.ValueKind != JsonValueKind.String || pluginVersion.GetString() != version)
        {
            throw new InvalidDataException("Unsupported worker protocol.");
        }

        if (reusePolicy == WorkerReusePolicy.Shared && (!frame.TryGetProperty("concurrentCalls", out var concurrent) || concurrent.ValueKind != JsonValueKind.Number || !concurrent.TryGetInt32(out int degreeRevision) || degreeRevision != 1))
        {
            throw new InvalidDataException("Worker does not support concurrent calls.");
        }

        if (reusePolicy == WorkerReusePolicy.ApprovedSessions && (!frame.TryGetProperty("sessionCleanup", out JsonElement cleanup) || cleanup.ValueKind != JsonValueKind.Number || !cleanup.TryGetInt32(out int revision) || revision != 1))
        {
            throw new InvalidDataException("Worker does not support approved session cleanup.");
        }
    }
}
