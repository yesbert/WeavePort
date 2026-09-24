using System.Text.Json;
using System.Text.Json.Serialization;
using WeavePort.Abstractions;

namespace WeavePort.Hosting;
internal sealed record CallbackResultFrame(string Type, string Id, string CallbackId, JsonElement Value);
