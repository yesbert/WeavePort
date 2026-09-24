using System.Text;

namespace WeavePort.Hosting;
internal static class ProcessDiagnostics
{
    private const int ReadBufferCharacters = 4096;
    private const int MaximumLineCharacters = 4096;
    internal static async Task DrainAsync(StreamReader reader, string instance, Action<string, string>? sink)
    {
        var buffer = new char[ReadBufferCharacters];
        StringBuilder? line = sink is null ? null : new StringBuilder(MaximumLineCharacters);
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            if (line is null)
            {
                continue;
            }

            DeliverLines(buffer, read, line, instance, sink!);
        }

        if (line is { Length: > 0 })
        {
            sink!(instance, line.ToString());
        }
    }

    private static void DeliverLines(char[] buffer, int read, StringBuilder line, string instance, Action<string, string> sink)
    {
        for (int index = 0; index < read; index++)
        {
            char value = buffer[index];
            if (value == '\n')
            {
                sink!(instance, line.ToString().TrimEnd('\r'));
                line.Clear();
                continue;
            }

            if (line.Length >= MaximumLineCharacters)
            {
                continue;
            }

            line.Append(value);
        }
    }
}
