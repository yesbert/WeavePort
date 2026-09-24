using System.Security.Cryptography;
using System.Text.Json;

namespace DecisionRoom.Host;

internal static class Wire
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    internal static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
