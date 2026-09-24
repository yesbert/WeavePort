using WeavePort.Internal;
using System.Text.Json;

namespace WeavePort.Hosting;
internal static partial class WorkerEnvelope
{
    private static readonly string[] ReservedNames = ["type", "id", "callbackId", "operation", "payload", "value", "protocol", "pluginVersion", "code", "sessionCleanup", "reusable", "concurrentCalls", "degree", "primaryCode", "cleanupFailed"];
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
        if (!frame.TryGetProperty(WireFields.Type, out JsonElement type) || type.ValueKind != JsonValueKind.String || type.GetString() != "ready" || !frame.TryGetProperty(WireFields.Protocol, out JsonElement protocol) || protocol.ValueKind != JsonValueKind.Number || !protocol.TryGetInt32(out int number) || number != (reusePolicy == WorkerReusePolicy.Shared ? ProtocolVersions.Concurrent : ProtocolVersions.Exclusive) || !frame.TryGetProperty(WireFields.PluginVersion, out JsonElement pluginVersion) || pluginVersion.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("Unsupported worker protocol.");
        }

        if (pluginVersion.GetString() != version)
        {
            throw new PluginVersionMismatchException(version, pluginVersion.GetString()!);
        }

        if (reusePolicy == WorkerReusePolicy.Shared && (!frame.TryGetProperty(WireFields.ConcurrentCalls, out var concurrent) || concurrent.ValueKind != JsonValueKind.Number || !concurrent.TryGetInt32(out int degreeRevision) || degreeRevision != ProtocolVersions.ConcurrentCalls))
        {
            throw new InvalidDataException("Worker does not support concurrent calls.");
        }

        if (reusePolicy == WorkerReusePolicy.ApprovedSessions && (!frame.TryGetProperty(WireFields.SessionCleanup, out JsonElement cleanup) || cleanup.ValueKind != JsonValueKind.Number || !cleanup.TryGetInt32(out int revision) || revision != ProtocolVersions.SessionCleanup))
        {
            throw new InvalidDataException("Worker does not support approved session cleanup.");
        }
    }
}
