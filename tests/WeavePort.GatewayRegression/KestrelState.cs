using System.Reflection;

// Version-specific inspection deliberately fails if the runtime layout changes.
internal static class KestrelState
{
    internal static object Field(object owner, string name)
    {
        for (Type? type = owner.GetType(); type is not null; type = type.BaseType)
        {
            if (type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) is { } field)
            {
                return field.GetValue(owner)!;
            }
        }

        throw new MissingFieldException(owner.GetType().FullName, name);
    }
    internal static int QueueCount(object writer) => (int)Field(Field(Field(writer, "_channel"), "_items"), "_size");
    internal static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
        {
            await Task.Delay(1, timeout.Token);
        }
    }
}
