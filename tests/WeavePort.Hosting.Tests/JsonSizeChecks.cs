using System.Text.Json;
using WeavePort.Internal;

internal static class JsonSizeChecks
{
    internal static int Run()
    {
        int checks = 0;
        string[] json = ["null", "true", "-0.00e+12", " { \"x\" : [1, 2] } ", "\"Grüße 🚀 <>&\\u0000\"", "{\"a\":1,\"a\":2}"];
        foreach (string source in json)
        {
            using var document = JsonDocument.Parse(source);
            Check(document.RootElement);
        }
        foreach (int limit in new[] { 128 << 10, 256 << 10, 512 << 10 })
        foreach (int delta in new[] { -1, 0, 1 })
        {
            JsonElement value = JsonSerializer.SerializeToElement(new string('x', limit - 2 + delta));
            if (JsonSize.Measure(value) != limit + delta) throw new Exception("Exact UTF-8 quota boundary changed.");
            Check(value);
        }
        var random = new Random(1729);
        for (int i = 0; i < 100; i++)
        {
            string value = string.Concat(Enumerable.Range(0, random.Next(1, 200)).Select(_ => "x<>&ü🚀\n\t\"\\"[random.Next(11)]));
            Check(JsonSerializer.SerializeToElement(new { value, i, nested = new[] { value } }));
        }
        Parallel.For(0, 100, i =>
        {
            var value = JsonSerializer.SerializeToElement(new { tenant = i, text = new string('x', i * 100) });
            if (JsonSize.Measure(value) != JsonSerializer.SerializeToUtf8Bytes(value).Length) throw new Exception("Concurrent size ownership.");
        });
        Console.WriteLine($"PASS {checks + 100} JSON size equivalence/boundary/concurrency assertions");
        return checks + 100;
        void Check(JsonElement value)
        {
            if (JsonSize.Measure(value) != JsonSerializer.SerializeToUtf8Bytes(value).Length) throw new Exception("Serialized byte length changed.");
            checks++;
        }
    }
}
