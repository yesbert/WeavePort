using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;

// Runs the same file functions in C#, Python and TypeScript workers against the real host.
internal static class FileChecks
{
    private static readonly string[] EntryNames = ["xl/workbook.xml", "xl/worksheets/sheet1.xml", "xl/media/blob.bin"];
    private static readonly byte[] Archive = CreateArchive();
    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new { });

    internal static async Task RunAsync(string[] args)
    {
        string root = Path.GetFullPath(args.ElementAtOrDefault(1) ?? Environment.CurrentDirectory);
        string? only = args.FirstOrDefault(argument => argument.StartsWith("--languages=", StringComparison.Ordinal));
        string[] languages = only is null ? ["csharp", "python", "typescript"] : only["--languages=".Length..].Split(',');
        foreach (string language in languages)
        {
            await CheckAsync(root, language);
            Console.WriteLine($"PASS function files: {language}");
        }
    }

    private static async Task CheckAsync(string root, string language)
    {
        await using var host = new PluginHost();
        IPluginSession session = await host.BindAsync(new PluginContext("tenant-" + language, "files", "1", "test", Empty), Profile(root, language), new NoCallbacks(), []);
        await using var client = new LocalPluginClient(session);
        var summary = await client.CallWithFilesAsync("zip-summary", Empty, new PluginFiles([Input()]));
        Check(summary.Value.GetProperty("names").EnumerateArray().Select(name => name.GetString()).SequenceEqual(EntryNames), language + " reads the ZIP directory at the end of a 9 MB input");
        JsonElement declared = summary.Value.GetProperty("inputs")[0];
        Check(declared.GetProperty("name").GetString() == "sheet" && declared.GetProperty("length").GetInt64() == Archive.Length && declared.GetProperty("mediaType").GetString() == "application/zip", language + " sees the host descriptors");

        var destination = new MemoryDestination();
        var copy = await client.CallWithFilesAsync("copy", Empty, new PluginFiles([Input()], destination, new PluginFileLimits(1, 16 << 20)));
        Check(copy.Outputs.Single() == new PluginOutputInfo("copy.bin", "application/octet-stream", Archive.Length) && SHA256.HashData(destination.Committed["copy.bin"]).AsSpan().SequenceEqual(SHA256.HashData(Archive)), language + " writes a byte-exact 9 MB output");

        string incomplete = await FailureAsync(() => client.CallWithFilesAsync("leave-open", Empty, new PluginFiles([], new MemoryDestination(), new PluginFileLimits(1, 100))));
        Check(incomplete == "output-incomplete", language + " output left open fails the call");
        var refusals = await client.CallWithFilesAsync("refusals", Empty, new PluginFiles([Input()]));
        Check(string.Join(",", refusals.Value.GetProperty("codes").EnumerateArray().Select(code => code.GetString())) == "input-not-found,output-not-permitted", language + " refuses unknown inputs and unpermitted outputs locally");
        var counting = new CountingReader(PluginInputReader.FromStream(new MemoryStream(Archive, writable: false)));
        await client.CallWithFilesAsync("cached-reads", Empty, new PluginFiles([new PluginInput("sheet", Archive.Length, "application/zip", counting)]));
        Check(counting.Reads == 1, language + " serves repeated reads within one block from its cache");
        var unaware = await client.CallAsync("refusals", Empty);
        Check(string.Join(",", unaware.GetProperty("codes").EnumerateArray().Select(code => code.GetString())) == "input-not-found,output-not-permitted", language + " refuses file use when the host sends no file descriptors");
        await client.CallWithFilesAsync("late-store", Empty, new PluginFiles([Input()]));
        var late = await client.CallWithFilesAsync("late-read", Empty, new PluginFiles([Input()]));
        Check(late.Value.GetProperty("refused").GetBoolean(), language + " refuses reads after the invocation ended");
        if (language == "python")
        {
            var loop = await client.CallWithFilesAsync("sync-on-loop", Empty, new PluginFiles([Input()]));
            Check(loop.Value.GetProperty("refused").GetBoolean(), "python refuses synchronous file use on the event loop instead of hanging");
        }
    }

    private static PluginInput Input() => new("sheet", Archive.Length, "application/zip", PluginInputReader.FromStream(new MemoryStream(Archive, writable: false)));

    private static ProcessProfile Profile(string root, string language)
    {
        ProcessProfile streams = StreamChecks.Profile(root, language);
        string[] arguments = language == "csharp"
            ? [typeof(FileChecks).Assembly.Location, "--file-worker"]
            : [.. streams.Arguments.Select(argument => argument.Replace(language == "python" ? "streams.py" : "streams.mjs", language == "python" ? "files.py" : "files.mjs", StringComparison.Ordinal))];
        return new ProcessProfile(streams.Executable, arguments, trustedCode: true, reservedMemoryMiB: 64, timeout: TimeSpan.FromSeconds(60));
    }

    private static byte[] CreateArchive()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string name in EntryNames)
            {
                using Stream entry = archive.CreateEntry(name, CompressionLevel.NoCompression).Open();
                entry.Write(RandomNumberGenerator.GetBytes(3 << 20));
            }
        }

        return buffer.ToArray();
    }

    private static async Task<string> FailureAsync(Func<Task> action)
    {
        try
        {
            await action();
            return "ok";
        }
        catch (PluginCallException error)
        {
            return error.ErrorCode;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("FAIL: " + message);
        }

        Console.WriteLine("PASS: " + message);
    }

    private sealed class MemoryDestination : IPluginOutputDestination
    {
        internal Dictionary<string, byte[]> Committed { get; } = [];

        public ValueTask<IPluginOutputWriter> CreateAsync(string name, string mediaType, CancellationToken cancellationToken) => ValueTask.FromResult<IPluginOutputWriter>(new Writer(this, name));

        private sealed class Writer(MemoryDestination owner, string name) : IPluginOutputWriter
        {
            private readonly MemoryStream _data = new();

            public ValueTask WriteAsync(ReadOnlyMemory<byte> block, CancellationToken cancellationToken)
            {
                _data.Write(block.Span);
                return ValueTask.CompletedTask;
            }

            public ValueTask CommitAsync(CancellationToken cancellationToken)
            {
                owner.Committed[name] = _data.ToArray();
                return ValueTask.CompletedTask;
            }

            public ValueTask AbortAsync(PluginOutputAbort reason, CancellationToken cancellationToken) => ValueTask.CompletedTask;
            public ValueTask DisposeAsync() => _data.DisposeAsync();
        }
    }

    private sealed class CountingReader(IPluginInputReader inner) : IPluginInputReader
    {
        internal int Reads;

        public ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Reads);
            return inner.ReadAsync(offset, buffer, cancellationToken);
        }
    }

    private sealed class NoCallbacks : IHostCallbacks
    {
        public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken) => throw new UnauthorizedAccessException();
    }
}
