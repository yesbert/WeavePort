using System.Text.Json;

internal static class RawFixture
{
    internal static void Run(string mode)
    {
        Console.WriteLine(mode == "legacy" ? "{\"type\":\"ready\",\"protocol\":1,\"pluginVersion\":\"1\"}" : "{\"type\":\"ready\",\"protocol\":1,\"pluginVersion\":\"1\",\"sessionCleanup\":1}");
        while(Console.ReadLine() is {} line)
        {
            string id=JsonDocument.Parse(line).RootElement.GetProperty("id").GetString()!;
            string suffix=mode switch { "missing"=>"", "invalid"=>",\"reusable\":\"yes\"", "duplicate"=>",\"reusable\":true,\"reusable\":true", _=>",\"reusable\":true" };
            Console.WriteLine("{\"type\":\"result\",\"id\":\""+id+"\",\"value\":{}"+suffix+"}");
        }
    }
}
