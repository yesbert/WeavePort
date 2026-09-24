using System.Text.Json;
using WeavePort.Internal;

namespace WeavePort.Hosting;
internal static partial class WorkerEnvelope
{
    internal static void ValidateExchange(JsonElement frame)
    {
        Validate(frame);
        RequireString(frame, WireFields.Type);
        RequireString(frame, WireFields.Id);
        string type = frame.GetProperty(WireFields.Type).GetString()!;
        if (type == FrameKinds.Result)
        {
            Require(frame, WireFields.Value);
            return;
        }

        if (type == FrameKinds.Callback)
        {
            RequireString(frame, WireFields.CallbackId);
            RequireString(frame, WireFields.Operation);
            Require(frame, WireFields.Payload);
            return;
        }

        if (type == FrameKinds.Error)
        {
            ValidateOptionalString(frame, WireFields.Code);
            ValidateOptionalString(frame, WireFields.PrimaryCode);
            return;
        }

        if (type != FrameKinds.Cancelled)
        {
            throw new InvalidDataException("Unsupported worker frame.");
        }
    }

    private static void Require(JsonElement frame, string name)
    {
        if (!frame.TryGetProperty(name, out _))
        {
            throw new InvalidDataException("Missing protocol field.");
        }
    }

    private static void RequireString(JsonElement frame, string name)
    {
        if (!frame.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("Missing or invalid protocol string.");
        }
    }

    private static void ValidateOptionalString(JsonElement frame, string name)
    {
        if (frame.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("Invalid protocol string.");
        }
    }
}
