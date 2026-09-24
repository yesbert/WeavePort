using System.Diagnostics;
using System.Text;

namespace WeavePort.Hosting;

internal static class DockerCommand
{
    internal static string ResolveExecutable(string? executable)
    {
        if (executable is not null)
        {
            return ValidateExecutable(executable);
        }

        string[] candidates = OperatingSystem.IsWindows() ? [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Docker", "Docker", "resources", "bin", "docker.exe")] : ["/usr/bin/docker", "/usr/local/bin/docker", "/Applications/Docker.app/Contents/Resources/bin/docker"];
        return candidates.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("Set DockerProfile.DockerExecutable to the absolute path of a trusted Docker CLI.");
    }

    internal static Process Start(DockerProfile profile, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(ResolveExecutable(profile.DockerExecutable))
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8
        };
        if (profile.Context is not null)
        {
            info.ArgumentList.Add("--context");
            info.ArgumentList.Add(profile.Context);
        }

        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return Process.Start(info) ?? throw new IOException("Docker did not start.");
    }

    internal static async Task<string> RunAsync(DockerProfile profile, string[] arguments, CancellationToken token)
    {
        using Process process = Start(profile, arguments);
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

    private static string ValidateExecutable(string executable)
    {
        if (!Path.IsPathFullyQualified(executable))
        {
            throw new ArgumentException("Docker executable must be an absolute path.", nameof(executable));
        }

        return File.Exists(executable) ? executable : throw new FileNotFoundException("Docker executable does not exist.", executable);
    }
}
