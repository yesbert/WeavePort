using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Internal;

namespace WeavePort.Hosting;
// Per-invocation file state. File operations run inline in the exchange loop under the invocation token;
// they never reach application callbacks, grants or the callback budget.
internal sealed partial class FileExchange(PluginFiles files)
{
    private static readonly PluginFiles NoFiles = new([]);
    private static readonly JsonElement Empty = JsonDocument.Parse("{}").RootElement.Clone();
    private readonly Dictionary<string, PluginInput> _inputs = files.Inputs.ToDictionary(input => input.Name, StringComparer.Ordinal);
    private long _readBudget = files.InputReadBudget;
    internal static FileExchange None() => new(NoFiles);

    internal static bool Handles(string operation) => operation is SdkOperations.InputRead or SdkOperations.OutputCreate or SdkOperations.OutputWrite or SdkOperations.OutputComplete;

    internal InvokeFiles Descriptor => new(
        [.. files.Inputs.Select(input => new InvokeInput(input.Name, input.Length, input.MediaType))],
        files.Outputs is null ? null : new InvokeOutputs(files.Limits.MaximumOutputs, files.Limits.MaximumOutputBytes));

    // Returns the callback value; FileRefusal becomes a callback-result error, PluginFileException fails the invocation.
    internal Task<JsonElement> HandleAsync(string operation, JsonElement payload, CancellationToken token)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("File operation payload must be an object.");
        }

        return operation switch
        {
            SdkOperations.InputRead => ReadAsync(payload, token),
            SdkOperations.OutputCreate => CreateAsync(payload, token),
            SdkOperations.OutputWrite => WriteAsync(payload, token),
            _ => Task.FromResult(Complete(payload))
        };
    }

    private async Task<JsonElement> ReadAsync(JsonElement payload, CancellationToken token)
    {
        string name = RequireString(payload, WireFields.Name);
        long offset = RequireInt64(payload, WireFields.Offset);
        long requested = RequireInt64(payload, WireFields.Length);
        if (!_inputs.TryGetValue(name, out PluginInput? input))
        {
            throw new FileRefusal(FileErrors.InputNotFound);
        }

        if (offset < 0 || requested is < ProtocolLimits.FileBlockMinimumBytes or > ProtocolLimits.FileBlockMaximumBytes)
        {
            throw new FileRefusal(FileErrors.InvalidRange);
        }

        int count = (int)Math.Clamp(input.Length - offset, 0, requested);
        if (count > _readBudget)
        {
            throw new PluginFileException(FailureCodes.InputReadLimit);
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Max(count, 1));
        try
        {
            await FillAsync(input.Reader, offset, buffer.AsMemory(0, count), token);
            _readBudget -= count;
            return JsonSerializer.SerializeToElement(new FileReadReply(buffer.AsMemory(0, count)), WireJson.Default.FileReadReply);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer.AsSpan(0, count));
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task FillAsync(IPluginInputReader reader, long offset, Memory<byte> destination, CancellationToken token)
    {
        int filled = 0;
        while (filled < destination.Length)
        {
            int read = await reader.ReadAsync(offset + filled, destination[filled..], token);
            if (read <= 0 || read > destination.Length - filled)
            {
                throw new IOException("Input reader did not honour the declared input length.");
            }

            filled += read;
        }
    }

    private static string RequireString(JsonElement payload, string name) => payload.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new InvalidDataException("Missing or invalid file field.");

    private static long RequireInt64(JsonElement payload, string name) => payload.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number) ? number : throw new InvalidDataException("Missing or invalid file field.");
}
