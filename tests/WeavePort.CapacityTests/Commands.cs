using System.Diagnostics;

namespace WeavePort.CapacityTests;

internal static class Commands
{
    internal static Process Start(string command, params string[] arguments)
    {
        var info = new ProcessStartInfo(command) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        return Process.Start(info) ?? throw new IOException("Could not start " + command);
    }

    internal static async Task<string> RunAsync(string command, params string[] arguments)
    {
        using Process process = Start(command, arguments);
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw new IOException(command + " timed out"); }
        if (process.ExitCode != 0) throw new IOException(command + " failed: " + await error);
        return await output;
    }
}
