using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Sdk.Client;

internal static class FileContractChecks
{
    private static readonly EmptyReader Reader = new();
    private static int _checks;

    internal static async Task<int> RunAsync()
    {
        _checks = 0;
        CheckDescriptors();
        CheckLimits();
        CheckFiles();
        await CheckLegacyClientAsync();
        return _checks;
    }

    private static void CheckDescriptors()
    {
        foreach (string name in new[] { "sheet", "result.xlsx", "a_b-c.1", new string('n', 128) })
        {
            Expect(new PluginInput(name, 0, "application/octet-stream", Reader).Name == name, "valid input name accepted");
        }

        foreach (string? name in new[] { null, "", ".hidden", "../x", "a/b", "a\\b", "with space", "\u00fcmlaut", new string('n', 129) })
        {
            Reject<ArgumentException>(() => new PluginInput(name!, 0, "text/plain", Reader));
        }

        foreach (string? mediaType in new[] { null, "", "text/plain; charset=utf-8", "text/\u0001", new string('m', 256) })
        {
            Reject<ArgumentException>(() => new PluginInput("x", 0, mediaType!, Reader));
        }

        Reject<ArgumentOutOfRangeException>(() => new PluginInput("x", -1, "text/plain", Reader));
        Reject<ArgumentNullException>(() => new PluginInput("x", 0, "text/plain", null!));
    }

    private static void CheckLimits()
    {
        Reject<ArgumentOutOfRangeException>(() => new PluginFileLimits(-1, 0));
        Reject<ArgumentOutOfRangeException>(() => new PluginFileLimits(65, 0));
        Reject<ArgumentOutOfRangeException>(() => new PluginFileLimits(1, -1));
        Reject<ArgumentOutOfRangeException>(() => new PluginFileLimits(1, 0, -1));
        Reject<ArgumentException>(() => new PluginFiles([Input("a", 1), Input("a", 2)]));
        Reject<ArgumentException>(() => new PluginFiles(Enumerable.Range(0, 65).Select(index => Input("i" + index, 1))));
        Reject<ArgumentException>(() => new PluginFiles([], new NoDestination()));
        Reject<ArgumentException>(() => new PluginFiles([], new NoDestination(), PluginFileLimits.InputsOnly));
    }

    private static void CheckFiles()
    {
        var source = new List<PluginInput> { Input("a", 10), Input("b", 5) };
        var files = new PluginFiles(source);
        source.Clear();
        Expect(files.Inputs.Count == 2 && files.Outputs is null && files.Limits == PluginFileLimits.InputsOnly, "inputs are copied and outputs default to none");
        Expect(files.InputReadBudget == 60, "default read budget is four times the declared lengths");
        Expect(new PluginFiles([Input("a", 10)], null, new PluginFileLimits(0, 0, 7)).InputReadBudget == 7, "explicit read budget wins");
        Expect(new PluginFiles([Input("a", long.MaxValue / 2), Input("b", long.MaxValue / 2)]).InputReadBudget == long.MaxValue, "read budget saturates");
        Expect(new PluginFiles([], new NoDestination(), new PluginFileLimits(2, 100)).Limits.MaximumOutputs == 2, "destination with output limits accepted");
        Expect(new InvocationResult("ok", default, "", 0).Outputs.Count == 0, "invocation outputs default to empty");
        Expect(new PluginCallResult<int>(1, null).Outputs.Count == 0, "call result outputs default to empty");
    }

    private static async Task CheckLegacyClientAsync()
    {
        IPluginClient legacy = new LegacyClient();
        try
        {
            await legacy.CallWithFilesAsync("op", JsonDocument.Parse("{}").RootElement, new PluginFiles([]));
        }
        catch (PluginCallException error) when (error.ErrorCode == "files-unsupported" && !error.MayHaveExecuted)
        {
            _checks++;
            return;
        }

        throw new Exception("Legacy client accepted a file call.");
    }

    private static PluginInput Input(string name, long length) => new(name, length, "application/octet-stream", Reader);

    private static void Expect(bool condition, string label)
    {
        if (!condition)
        {
            throw new Exception("File contract: " + label);
        }

        _checks++;
    }

    private static void Reject<TException>(Func<object> action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException) { _checks++; return; }
        throw new Exception("Invalid file descriptor was accepted.");
    }

    private sealed class EmptyReader : IPluginInputReader
    {
        public ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken) => ValueTask.FromResult(0);
    }

    private sealed class NoDestination : IPluginOutputDestination
    {
        public ValueTask<IPluginOutputWriter> CreateAsync(string name, string mediaType, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class LegacyClient : IPluginClient
    {
        public Task<JsonElement> CallAsync(string operation, JsonElement input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<JsonElement> StreamAsync(string operation, JsonElement input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
