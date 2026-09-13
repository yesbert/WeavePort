using System.Diagnostics;
using System.Text;

namespace WeavePort.Hosting;
internal static class DockerCommand
{
    internal static Process Start(string? context, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo("docker")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8
        };
        if (context is not null)
        {
            info.ArgumentList.Add("--context");
            info.ArgumentList.Add(context);
        }

        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return Process.Start(info) ?? throw new IOException("Docker did not start.");
    }

    internal static async Task<string> RunAsync(string? context, string[] arguments, CancellationToken token)
    {
        using Process process = Start(context, arguments);
        Task<string> output = process.StandardOutput.ReadToEndAsync(token);
        Task<string> error = process.StandardError.ReadToEndAsync(token);
        try
        {
            await process.WaitForExitAsync(token);
            string result = await output;
            string diagnostic = await error;
            if (process.ExitCode != 0)
            {
                throw new IOException($"Docker command failed: {diagnostic.Trim()}");
            }

            return result.Trim();
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }

            throw;
        }
    }
}
