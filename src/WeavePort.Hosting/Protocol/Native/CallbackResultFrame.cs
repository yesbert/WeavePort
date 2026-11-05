using System.Text.Json;
using System.Text.Json.Serialization;
using WeavePort.Abstractions;

namespace WeavePort.Hosting;

internal sealed record CallbackResultFrame(string Type, string Id, string CallbackId, JsonElement Value, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Error = null);
internal sealed record FileReadReply(ReadOnlyMemory<byte> Data);
internal sealed record FileCompleteReply(long Length);
