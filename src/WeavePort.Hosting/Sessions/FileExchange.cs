using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Internal;

namespace WeavePort.Hosting;
// Per-invocation file state. File operations run inline in the exchange loop under the invocation token,
// and they never reach application callbacks, grants or the callback budget.
internal sealed partial class FileExchange(PluginFiles files)
{
    private static readonly PluginFiles NoFiles = new([]);
    private static readonly JsonElement Empty = JsonDocument.Parse("{}").RootElement.Clone();
    private readonly Dictionary<string, PluginInput> _inputs = files.Inputs.ToDictionary(input => input.Name, StringComparer.Ordinal);
    private long _readBudget = files.InputReadBudget;
    internal static FileExchange None() => new(NoFiles);

    // Files go only to function calls; stream and source operations refuse them before dispatch.
    internal static bool RefusesOperation(string operation) => operation.StartsWith("$sdk.", StringComparison.Ordinal) && operation != SdkOperations.Call;

    internal static bool Handles(string operation) => operation is SdkOperations.InputRead or SdkOperations.OutputCreate or SdkOperations.OutputWrite or SdkOperations.OutputComplete;

    internal InvokeFiles Descriptor => new(
        [.. files.Inputs.Select(input => new InvokeInput(input.Name, input.Length, input.MediaType))],
        files.Outputs is null ? null : new InvokeOutputs(files.Limits.MaximumOutputs, files.Limits.MaximumOutputBytes));

    // Returns the callback reply; FileRefusalException becomes a callback-result error, PluginFileException fails the invocation.
    internal async Task<FileReply> HandleAsync(string operation, JsonElement payload, CancellationToken token)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("File operation payload must be an object.");
        }

        return operation switch
        {
            SdkOperations.InputRead => await ReadAsync(payload, token),
            SdkOperations.OutputCreate => new FileReply(await CreateAsync(payload, token)),
            SdkOperations.OutputWrite => new FileReply(await WriteAsync(payload, token)),
            _ => new FileReply(Complete(payload))
        };
    }

    // Reads into a rented buffer that the caller writes to the worker once and then releases.
    // Every read is charged at least one minimum block, so repeated reads at or past the end still exhaust the budget.
    private async Task<FileReply> ReadAsync(JsonElement payload, CancellationToken token)
    {
        string name = RequireString(payload, WireFields.Name);
        long offset = RequireInt64(payload, WireFields.Offset);
        long requested = RequireInt64(payload, WireFields.Length);
        if (!_inputs.TryGetValue(name, out PluginInput? input))
        {
            throw new FileRefusalException(FileErrors.InputNotFound);
        }

        if (offset < 0 || requested is < ProtocolLimits.FileBlockMinimumBytes or > ProtocolLimits.FileBlockMaximumBytes)
        {
            throw new FileRefusalException(FileErrors.InvalidRange);
        }

        int count = (int)Math.Clamp(input.Length - offset, 0, requested);
        long charge = count == 0 ? ProtocolLimits.FileBlockMinimumBytes : count;
        if (charge > _readBudget)
        {
            throw new PluginFileException(FailureCodes.InputReadLimit);
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Max(count, 1));
        try
        {
            await PositionalRead.FillAsync(input.Reader, offset, buffer.AsMemory(0, count), token);
        }
        catch
        {
            FileReply.Release(buffer, count);
            throw;
        }

        _readBudget -= charge;
        return new FileReply(default, buffer, count);
    }

    private static string RequireString(JsonElement payload, string name) => payload.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new InvalidDataException("Missing or invalid file field.");

    private static long RequireInt64(JsonElement payload, string name) => payload.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number) ? number : throw new InvalidDataException("Missing or invalid file field.");
}

// A file operation's reply: a JSON value, or a rented input block the caller writes once and then releases.
internal readonly record struct FileReply(JsonElement Value, byte[]? Rented = null, int Count = 0)
{
    internal ReadOnlyMemory<byte> Data => Rented.AsMemory(0, Count);

    internal void Release()
    {
        if (Rented is not null)
        {
            Release(Rented, Count);
        }
    }

    internal static void Release(byte[] buffer, int count)
    {
        CryptographicOperations.ZeroMemory(buffer.AsSpan(0, count));
        ArrayPool<byte>.Shared.Return(buffer);
    }
}
