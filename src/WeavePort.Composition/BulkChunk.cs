using System.Text.Json.Serialization;

namespace WeavePort.Composition;

internal sealed record BulkChunk([property: JsonPropertyName("data")] ReadOnlyMemory<byte> Data);
