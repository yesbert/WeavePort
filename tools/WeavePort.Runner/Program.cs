using System.Diagnostics;
using System.Text.Json;
using WeavePort.Runner;

if (args.Length < 2 || args[0] is not ("verify" or "load"))
{
    throw new ArgumentException("Expected verify|load followed by repository root.");
}

TimeProvider clock = TimeProvider.System;
string root = Path.GetFullPath(args[1]);
string mode = args[0];
string output = Path.Combine(root, "artifacts", "runs", mode == "verify" ? "verification" : "load", clock.GetUtcNow().ToString("yyyyMMdd-HHmmss-ffffff"));
Directory.CreateDirectory(output);
await Provenance.WriteAsync(root, output, mode, clock);
var info = new ProcessStartInfo("dotnet") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
info.Environment["WEAVEPORT_ROOT"] = root;
info.ArgumentList.Add(Path.Combine(root, mode == "verify" ? "tests/WeavePort.Docker.Tests/bin/Release/net10.0/WeavePort.Demo.dll" : "tests/WeavePort.LoadTests/bin/Release/net10.0/WeavePort.LoadTests.dll"));
info.ArgumentList.Add(root);
info.ArgumentList.Add(output);
using Process process = Process.Start(info) ?? throw new IOException("Could not launch .NET.");
using var log = TextWriter.Synchronized(new StreamWriter(Path.Combine(output, "console.log")) { AutoFlush = true });
Task stdout = CopyAsync(process.StandardOutput);
Task stderr = CopyAsync(process.StandardError);
await process.WaitForExitAsync();
await Task.WhenAll(stdout, stderr);
await File.WriteAllTextAsync(Path.Combine(output, "exit-code.txt"), process.ExitCode + Environment.NewLine);
Console.WriteLine("Evidence directory: " + Path.GetRelativePath(root, output));
Environment.ExitCode = process.ExitCode;

async Task CopyAsync(StreamReader reader)
{
    while (await reader.ReadLineAsync() is { } line)
    {
        string portable = line.Replace(root, ".", StringComparison.Ordinal);
        Console.WriteLine(portable);
        await log.WriteLineAsync(portable);
    }
}
