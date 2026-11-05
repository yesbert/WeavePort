using System.Text.Json;
using System.Text.Json.Serialization;
using WeavePort.Abstractions;

namespace WeavePort.Hosting;

internal sealed record InvokeFrame(string Type, string Id, string Operation, JsonElement Payload, PluginContext Context, string TraceId, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] InvokeFiles? Files = null);
internal sealed record InvokeFiles(IReadOnlyList<InvokeInput> Inputs, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] InvokeOutputs? Outputs);
internal sealed record InvokeInput(string Name, long Length, string MediaType);
internal sealed record InvokeOutputs(int MaximumCount, long MaximumBytes);
