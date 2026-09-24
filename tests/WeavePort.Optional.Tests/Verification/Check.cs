using System.Text.Json;

internal static class Check
{
    internal static int Count { get; private set; }
    internal static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);
    internal static void That(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidDataException(name);
        }

        Count++;
        Console.WriteLine("PASS: " + name);
    }
    internal static async Task<T> Fails<T>(Func<Task> action, string name) where T : Exception
    {
        try
        {
            await action().WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (T error) { That(true, name); return error; }
        throw new InvalidDataException("Expected " + typeof(T).Name + ": " + name);
    }
}
