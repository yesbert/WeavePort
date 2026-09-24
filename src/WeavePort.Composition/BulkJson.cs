using System.Text.Json.Serialization;

namespace WeavePort.Composition;
[JsonSerializable(typeof(BulkChunk))]
internal sealed partial class BulkJson : JsonSerializerContext;
