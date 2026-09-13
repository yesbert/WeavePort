using System.Diagnostics;

internal static class ResourceChecks
{
    internal static async Task<string> DockerAsync(string[] args)
    {
        var info = new ProcessStartInfo("docker") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in args) info.ArgumentList.Add(arg);
        using Process process = Process.Start(info)!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Task<string> output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        Task<string> error = process.StandardError.ReadToEndAsync(deadline.Token);
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); throw; }
        string result = await output;
        if (process.ExitCode != 0) throw new IOException("Docker observation failed: " + await error);
        return result.Trim();
    }
}
