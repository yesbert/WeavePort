using System.Text.Json;
using WeavePort.Sdk.Client;

namespace WeavePort.SdkFixture;

public static class Workload
{
    public static readonly string[] Cases = ["tiny", "callback", "list-64k", "list-8m"];
    public static JsonElement Query(int count, int width, int delayMs = 0, int failAt = -1, bool callbacks = false) => JsonSerializer.SerializeToElement(new { count, width, delayMs, failAt, callbacks });
    public static async Task<JsonElement[]> ExecuteAsync(IPluginClient client, string name, CancellationToken token = default)
    {
        if (name == "tiny") return [await client.CallAsync("echo", JsonSerializer.SerializeToElement(new { value = "sdk" }), token)];
        if (name == "callback") return [await client.CallAsync("owner", JsonSerializer.SerializeToElement(new { operation = "host.owner", input = new { tenant = "forged" } }), token)];
        var items = new List<JsonElement>();
        await foreach (JsonElement item in client.StreamAsync("records", Query(name == "list-64k" ? 8 : 1024, 8192), token)) items.Add(item);
        return items.ToArray();
    }
    public static void Check(JsonElement[] output, string name, string tenant)
    {
        if (name == "tiny") { if (output.Length != 1 || output[0].GetProperty("value").GetString() != "sdk") throw new InvalidDataException("Echo mismatch."); return; }
        if (name == "callback") { if (output.Length != 1 || output[0].GetProperty("owner").GetString() != tenant || output[0].GetProperty("secret").GetString() != "synthetic-" + tenant) throw new InvalidDataException("Callback authority mismatch."); return; }
        int count = name == "list-64k" ? 8 : 1024;
        if (output.Length != count) throw new InvalidDataException("Record count.");
        for (int i = 0; i < output.Length; i++) CheckRow(output[i], i, 8192, tenant);
    }
    public static void CheckRow(JsonElement item, int id, int width, string tenant)
    {
        string text = item.GetProperty("text").GetString()!;
        if (item.GetProperty("id").GetInt32() != id || item.GetProperty("owner").GetString() != tenant || text.Length != width || text.AsSpan().ContainsAnyExcept('x'))
            throw new InvalidDataException("Record mismatch.");
    }
}
