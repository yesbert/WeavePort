using WeavePort.Hosting;
using System.Text.Json;
using DocumentWorkshop.Contracts;
using WeavePort.Abstractions;

namespace DocumentWorkshop.Host;

internal static class Json
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
