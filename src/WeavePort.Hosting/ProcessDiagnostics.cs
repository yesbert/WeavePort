using System.Text;

namespace WeavePort.Hosting;
internal static class ProcessDiagnostics
{
    internal static async Task DrainAsync(StreamReader reader, string instance, Action<string, string>? sink)
    {
        var buffer = new char[4096];
        StringBuilder? line = sink is null ? null : new StringBuilder(4096);
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            if (line is null)
            {
                continue;
            }

            for (int index = 0; index < read; index++)
            {
                char value = buffer[index];
                if (value == '\n')
                {
                    sink!(instance, line.ToString().TrimEnd('\r'));
                    line.Clear();
                }
                else if (line.Length < 4096)
                {
                    line.Append(value);
                }
            }
        }

        if (line is { Length: > 0 })
        {
            sink!(instance, line.ToString());
        }
    }
}
