using System.Text.Json;
using WeavePort.Hosting;

internal static class EnvelopeChecks
{
    internal static int Run()
    {
        int checks = 0;
        foreach (string name in new[] { "type", "id", "callbackId", "operation", "payload", "value", "protocol", "pluginVersion", "code" })
        {
            Reject(() => WorkerEnvelope.Validate(JsonElement.Parse($$"""{"{{name}}":1,"{{name}}":2}""")));
        }
        Reject(() => WorkerEnvelope.Validate(JsonElement.Parse("{\"id\":1,\"\\u0069d\":2}")));
        foreach (string version in new[] { "1.5", "1e999", "2147483648", "null", "[]", "\"1\"", "2" })
        {
            Reject(() => WorkerEnvelope.ValidateReady(JsonElement.Parse($$"""{"type":"ready","protocol":{{version}},"pluginVersion":"1"}"""), "1"));
        }

        foreach (string invalid in new[] { "null", "[]", "42", "{}", "{\"type\":false}", "{\"type\":\"ready\",\"protocol\":1,\"pluginVersion\":{}}" })
        {
            Reject(() => WorkerEnvelope.ValidateReady(JsonElement.Parse(invalid), "1"));
        }

        WorkerEnvelope.ValidateReady(JsonElement.Parse("{\"type\":\"ready\",\"protocol\":1,\"pluginVersion\":\"1\"}"), "1");
        checks++;
        WorkerEnvelope.Validate(JsonElement.Parse("{\"type\":\"result\",\"id\":\"call\",\"value\":{\"id\":1,\"id\":2}}"));
        checks++;
        return checks;

        void Reject(Action action)
        {
            try
            {
                action();
            }
            catch (InvalidDataException) { checks++; return; }
            throw new Exception("Hostile envelope was accepted");
        }
    }
}
