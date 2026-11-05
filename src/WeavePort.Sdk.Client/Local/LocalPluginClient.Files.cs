using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Internal;

namespace WeavePort.Sdk.Client;

public sealed partial class LocalPluginClient
{
    /// <inheritdoc/>
    public async Task<PluginCallResult<JsonElement>> CallWithFilesAsync(string operation, JsonElement input, PluginFiles files, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        using var stop = CreateOperationSource(cancellationToken);
        if (session is not IPluginFileSession fileSession)
        {
            throw new PluginCallException(FailureCodes.FilesUnsupported, false);
        }

        CheckInput(input);
        stop.Token.ThrowIfCancellationRequested();
        InvocationResult reply = await fileSession.InvokeAsync(SdkOperations.Call, JsonSerializer.SerializeToElement(new
        {
            operation,
            input
        }), files, stop.Token);
        if (reply.Status == FailureCodes.Cancelled)
        {
            throw new OperationCanceledException(stop.Token);
        }

        if (reply.Status != FailureCodes.Ok)
        {
            throw new PluginCallException(reply.Status, reply.MayHaveExecuted)
            {
                Failure = reply.Failure,
                VersionMismatch = reply.VersionMismatch,
                Outputs = reply.Outputs
            };
        }

        if (Size(reply.Value) > ProtocolLimits.UnaryBytes)
        {
            throw new PluginCallException(FailureCodes.ValueLimit);
        }

        return new(reply.Value, reply.ElapsedMs)
        {
            Outputs = reply.Outputs
        };
    }
}
