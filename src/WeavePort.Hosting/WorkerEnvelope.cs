using System.Text.Json;

namespace WeavePort.Hosting;
internal static class WorkerEnvelope
{
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
            int flag = property.NameEquals("type"u8) ? 1 : property.NameEquals("id"u8) ? 2 : property.NameEquals("callbackId"u8) ? 4 : property.NameEquals("operation"u8) ? 8 : property.NameEquals("payload"u8) ? 16 : property.NameEquals("value"u8) ? 32 : property.NameEquals("protocol"u8) ? 64 : property.NameEquals("pluginVersion"u8) ? 128 : property.NameEquals("code"u8) ? 256 : 0;
            if ((seen & flag) != 0)
            {
                throw new InvalidDataException("Repeated reserved protocol field.");
            }

            seen |= flag;
        }
    }

    internal static void ValidateReady(JsonElement frame, string version)
    {
        Validate(frame);
        if (!frame.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.String || type.GetString() != "ready" || !frame.TryGetProperty("protocol", out JsonElement protocol) || protocol.ValueKind != JsonValueKind.Number || !protocol.TryGetInt32(out int number) || number != 1 || !frame.TryGetProperty("pluginVersion", out JsonElement pluginVersion) || pluginVersion.ValueKind != JsonValueKind.String || pluginVersion.GetString() != version)
        {
            throw new InvalidDataException("Unsupported worker protocol.");
        }
    }
}
