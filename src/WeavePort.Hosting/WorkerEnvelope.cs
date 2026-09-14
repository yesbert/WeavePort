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
        if (property.NameEquals("type"u8))
        {
            return 1;
        }

        if (property.NameEquals("id"u8))
        {
            return 2;
        }

        if (property.NameEquals("callbackId"u8))
        {
            return 4;
        }

        if (property.NameEquals("operation"u8))
        {
            return 8;
        }

        if (property.NameEquals("payload"u8))
        {
            return 16;
        }

        if (property.NameEquals("value"u8))
        {
            return 32;
        }

        if (property.NameEquals("protocol"u8))
        {
            return 64;
        }

        if (property.NameEquals("pluginVersion"u8))
        {
            return 128;
        }

        if (property.NameEquals("code"u8))
        {
            return 256;
        }

        return 0;
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
