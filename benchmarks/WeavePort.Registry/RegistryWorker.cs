using System.Text.Json;

internal static class RegistryWorker
{
    internal static async Task RunAsync()
    {
        Console.WriteLine("{\"type\":\"ready\",\"protocol\":1,\"pluginVersion\":\"1\"}");
        while (await Console.In.ReadLineAsync() is { } line)
        {
            using var document = JsonDocument.Parse(line);
            var frame = document.RootElement;
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                type = "result",
                id = frame.GetProperty("id").GetString(),
                value = new
                {
                    tenant = frame.GetProperty("context").GetProperty("tenant").GetString()
                }
            }));
        }
    }
}
