using Grpc.Core;
using WeavePort.Abstractions;
using WeavePort.Internal;
using WeavePort.Sdk.Client;
using WeavePort.Sdk.Gateway.Protocol;

namespace WeavePort.Sdk.Gateway;

public sealed partial class GatewayService
{
    // Runs a function call whose inputs and outputs live at the remote caller. The host's readers and
    // writers relay over this session; the gateway never persists file bytes.
    private async Task<Reply> FileCallAsync(Request request, IAsyncStreamReader<Request> input, IServerStreamWriter<Reply> output, ServerCallContext context)
    {
        string? credential = context.RequestHeaders.GetValue(GatewayMetadata.BindingCredential);
        IPluginClient client = Authorize(context);
        PluginFileLimits allowed = registry.GetFileLimits(credential) ?? throw new PluginCallException(FailureCodes.FilesUnsupported, false);
        var relay = new FileRelay(input, output, context.CancellationToken);
        PluginFiles files = Files(request, allowed, relay);
        PluginCallResult<System.Text.Json.JsonElement> result;
        try
        {
            result = await client.CallWithFilesAsync(request.Operation, Decode(request), files, context.CancellationToken);
        }
        catch (Exception error) when (relay.Broken && !context.CancellationToken.IsCancellationRequested)
        {
            // An interrupted relay leaves the request stream out of step. Report the call's own outcome
            // (for example timeout) as the terminal reply, then end the session instead of reusing it.
            Reply terminal = EncodeFailure(error);
            terminal.Complete = true;
            await output.WriteAsync(terminal, context.CancellationToken);
            throw new FileRelayBrokenException();
        }

        relay.ThrowIfBroken();

        Reply reply = Encode(result.Value);
        if (result.ElapsedMs is { } elapsed)
        {
            reply.ElapsedMs = elapsed;
        }

        reply.Outputs.Add(result.Outputs.Select(item => new FileOutput { Name = item.Name, MediaType = item.MediaType, Length = item.Length }));
        return reply;
    }

    // The registration caps what a credential holder may request; a request above it is refused before dispatch.
    private static PluginFiles Files(Request request, PluginFileLimits allowed, FileRelay relay)
    {
        PluginInput[] inputs;
        try
        {
            inputs = [.. request.Inputs.Select(item => new PluginInput(item.Name, item.Length, item.MediaType, new FileRelay.Reader(relay, item.Name)))];
        }
        catch (ArgumentException)
        {
            throw new PluginCallException(FailureCodes.InvalidArgument, false);
        }

        FileLimits requested = request.FileLimits ?? new FileLimits();
        bool outputs = requested.Outputs;
        if (outputs && (allowed.MaximumOutputs == 0 || requested.MaximumOutputs < 1 || requested.MaximumOutputs > allowed.MaximumOutputs || requested.MaximumOutputBytes < 0 || requested.MaximumOutputBytes > allowed.MaximumOutputBytes))
        {
            throw new PluginCallException(FailureCodes.OutputLimit, false);
        }

        long? budget = requested.HasMaximumInputReadBytes ? requested.MaximumInputReadBytes : allowed.MaximumInputReadBytes;
        var limits = new PluginFileLimits(outputs ? requested.MaximumOutputs : 0, outputs ? requested.MaximumOutputBytes : 0, budget is < 0 ? throw new PluginCallException(FailureCodes.InvalidArgument, false) : budget);
        PluginFiles files = TryFiles(inputs, outputs ? new FileRelay.Destination(relay) : null, limits);
        long ceiling = allowed.MaximumInputReadBytes is { } fixedCeiling ? fixedCeiling : new PluginFiles(inputs).InputReadBudget;
        return files.InputReadBudget > ceiling ? throw new PluginCallException(FailureCodes.InputReadLimit, false) : files;
    }

    private static PluginFiles TryFiles(PluginInput[] inputs, IPluginOutputDestination? destination, PluginFileLimits limits)
    {
        try
        {
            return new PluginFiles(inputs, destination, limits);
        }
        catch (ArgumentException)
        {
            throw new PluginCallException(FailureCodes.InvalidArgument, false);
        }
    }
}
