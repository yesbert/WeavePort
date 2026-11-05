using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WeavePort.Abstractions;
using WeavePort.Internal;

namespace WeavePort.Hosting;
// Outputs are appended sequentially, committed only after the invocation succeeded and aborted otherwise.
internal sealed partial class FileExchange
{
    private readonly List<OutputState> _outputs = [];
    private long _outputBytes;
    private bool _released;

    private async Task<JsonElement> CreateAsync(JsonElement payload, CancellationToken token)
    {
        string name = RequireString(payload, WireFields.Name);
        string mediaType = RequireString(payload, WireFields.MediaType);
        if (!FileDescriptor.IsValidName(name) || !FileDescriptor.IsValidMediaType(mediaType))
        {
            throw new InvalidDataException("Invalid output descriptor.");
        }

        if (files.Outputs is null)
        {
            throw new FileRefusal(FileErrors.OutputNotPermitted);
        }

        if (Find(name) is not null)
        {
            throw new FileRefusal(FileErrors.OutputExists);
        }

        if (_outputs.Count >= files.Limits.MaximumOutputs)
        {
            throw new PluginFileException(FailureCodes.OutputLimit);
        }

        IPluginOutputWriter writer = await files.Outputs.CreateAsync(name, mediaType, token) ?? throw new InvalidOperationException("Output destination returned no writer.");
        _outputs.Add(new OutputState(name, mediaType, writer));
        return Empty;
    }

    private async Task<JsonElement> WriteAsync(JsonElement payload, CancellationToken token)
    {
        OutputState output = Open(RequireString(payload, WireFields.Name));
        if (!payload.TryGetProperty(WireFields.Data, out JsonElement encoded) || encoded.ValueKind != JsonValueKind.String || !encoded.TryGetBytesFromBase64(out byte[]? block) || block.Length is 0 or > ProtocolLimits.FileBlockMaximumBytes)
        {
            throw new InvalidDataException("Invalid output block.");
        }

        try
        {
            if (block.Length > files.Limits.MaximumOutputBytes - _outputBytes)
            {
                throw new PluginFileException(FailureCodes.OutputLimit);
            }

            await output.Writer.WriteAsync(block, token);
            _outputBytes += block.Length;
            output.Length += block.Length;
            return Empty;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(block);
        }
    }

    private JsonElement Complete(JsonElement payload)
    {
        OutputState output = Open(RequireString(payload, WireFields.Name));
        output.Completed = true;
        return JsonSerializer.SerializeToElement(new FileCompleteReply(output.Length), WireJson.Default.FileCompleteReply);
    }

    private OutputState Open(string name)
    {
        OutputState output = Find(name) ?? throw new FileRefusal(FileErrors.OutputNotPermitted);
        return output.Completed ? throw new FileRefusal(FileErrors.OutputClosed) : output;
    }

    private OutputState? Find(string name) => _outputs.Find(output => output.Name == name);

    // Commits every output in creation order once the invocation succeeded.
    internal async Task<IReadOnlyList<PluginOutputInfo>> CommitAsync(CancellationToken token)
    {
        if (_outputs.Exists(output => !output.Completed))
        {
            throw new PluginFileException(FailureCodes.OutputIncomplete);
        }

        var committed = new List<PluginOutputInfo>(_outputs.Count);
        foreach (OutputState output in _outputs)
        {
            try
            {
                await output.Writer.CommitAsync(token);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                throw new OutputCommitException(committed.AsReadOnly(), error);
            }

            output.Committed = true;
            committed.Add(new PluginOutputInfo(output.Name, output.MediaType, output.Length));
        }

        return committed.AsReadOnly();
    }

    // Aborts every uncommitted output when a reason is given, then disposes all writers once.
    // Returns whether any abort or disposal failed; never throws, and runs under a bounded grace period.
    internal async Task<bool> ReleaseAsync(PluginOutputAbort? reason, ILogger logger, string instance)
    {
        if (_released)
        {
            return false;
        }

        _released = true;
        bool failed = false;
        using var grace = new CancellationTokenSource(InvocationPolicy.CleanupGrace);
        foreach (OutputState output in _outputs)
        {
            failed |= !await TryAsync(() => reason is { } abort && !output.Committed ? output.Writer.AbortAsync(abort, grace.Token) : ValueTask.CompletedTask, grace.Token, logger, instance);
            failed |= !await TryAsync(() => output.Writer.DisposeAsync(), grace.Token, logger, instance);
        }

        return failed;
    }

    // Waits for one writer step at most until the shared grace deadline. A step still running then is left
    // to finish on its own, its eventual fault observed, and reported as a cleanup failure.
    private static async Task<bool> TryAsync(Func<ValueTask> action, CancellationToken grace, ILogger logger, string instance)
    {
        Task step;
        try
        {
            step = action().AsTask();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            RuntimeLog.CleanupFailed(logger, instance, error.GetType().Name);
            return false;
        }

        try
        {
            await step.WaitAsync(grace);
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _ = step.ContinueWith(static task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            RuntimeLog.CleanupFailed(logger, instance, error.GetType().Name);
            return false;
        }
    }

    internal static PluginOutputAbort AbortReason(string code) => code switch
    {
        FailureCodes.Cancelled or FailureCodes.Timeout or FailureCodes.Disabled => PluginOutputAbort.Cancelled,
        FailureCodes.OutputLimit or FailureCodes.InputReadLimit => PluginOutputAbort.Limit,
        FailureCodes.OutputCommitFailed => PluginOutputAbort.CommitFailed,
        _ => PluginOutputAbort.Failed
    };

    private sealed class OutputState(string name, string mediaType, IPluginOutputWriter writer)
    {
        internal string Name { get; } = name;
        internal string MediaType { get; } = mediaType;
        internal IPluginOutputWriter Writer { get; } = writer;
        internal long Length { get; set; }
        internal bool Completed { get; set; }
        internal bool Committed { get; set; }
    }
}
