using System.Diagnostics;
using System.Text.Json;
using WeavePort.Hosting;

namespace WeavePort.LocalTools;

internal sealed record LocalConfiguration(string Dotnet, string Python, string Node, string Csharp, string PythonScript,
    string TypeScriptScript, string WorkspaceRoot, bool SelfContained = false, bool UseUnixSocket = false, int? SocketBufferBytes = null)
{
    internal static LocalConfiguration Load(string path)
    {
        LocalConfiguration value = JsonSerializer.Deserialize<LocalConfiguration>(File.ReadAllText(path))!;
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        string Resolve(string item) => item.Length == 0 ? "" : Path.GetFullPath(item, directory);
        int? socketBufferBytes = Environment.GetEnvironmentVariable("WEAVEPORT_LOCAL_SOCKET_BUFFER_BYTES") is { } bytes
            ? int.Parse(bytes) : value.SocketBufferBytes;
        bool useSocket = Environment.GetEnvironmentVariable("WEAVEPORT_LOCAL_TRANSPORT") switch
        {
            null => value.UseUnixSocket,
            "stdio" => false,
            "socket" => true,
            _ => throw new ArgumentException("Unknown native transport")
        };
        return value with
        {
            Dotnet = Resolve(value.Dotnet),
            Python = Resolve(value.Python),
            Node = Resolve(value.Node),
            Csharp = Resolve(value.Csharp),
            PythonScript = Resolve(value.PythonScript),
            TypeScriptScript = Resolve(value.TypeScriptScript),
            WorkspaceRoot = Resolve(value.WorkspaceRoot),
            SocketBufferBytes = socketBufferBytes,
            UseUnixSocket = useSocket
        };
    }

    internal ProcessProfile Profile(string language, TimeSpan? timeout = null, TimeSpan? idleTimeout = null) => (language switch
    {
        "csharp" => new ProcessProfile(SelfContained ? Csharp : Dotnet, SelfContained ? [] : [Csharp], true, WorkspaceRoot, timeout: timeout, idleTimeout: idleTimeout),
        "python" => new ProcessProfile(Python, ["-I", "-u", PythonScript], true, WorkspaceRoot, timeout: timeout, idleTimeout: idleTimeout),
        "typescript" => new ProcessProfile(Node, [TypeScriptScript], true, WorkspaceRoot, timeout: timeout, idleTimeout: idleTimeout),
        _ => throw new ArgumentException("Unknown local fixture language")
    }) with
    {
        UseUnixSocket = UseUnixSocket,
        SocketBufferBytes = SocketBufferBytes
    };

    internal static string Find(string executable)
    {
        foreach (string entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            string candidate = Path.Combine(entry, executable + (OperatingSystem.IsWindows() ? ".exe" : ""));
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }
        throw new FileNotFoundException("Required executable not found: " + executable);
    }

    internal static async Task<string> VersionAsync(string executable, params string[] arguments)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(info) ?? throw new IOException("Prerequisite check did not start");
        Task<string> output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        Task<string> error = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            string text = (await output + await error).Trim();
            if (process.ExitCode != 0)
            {
                throw new IOException("Prerequisite command failed");
            }

            return text;
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }
}
