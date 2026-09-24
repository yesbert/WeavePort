using WeavePort.Internal;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using WeavePort.Sdk.Client;
using WeavePort.Sdk.Gateway.Protocol;

namespace WeavePort.Sdk.Gateway;
/// <summary>Shared worker-host transport. Register the registry and map this gRPC service in the trusted application.</summary>
public sealed partial class GatewayService(GatewayRegistry registry) : WorkerGateway.WorkerGatewayBase
{
    /// <summary>Binding-scoped sequential exchanges on a persistent HTTP/2 stream.</summary>
    public override async Task Session(IAsyncStreamReader<Request> requestStream, IServerStreamWriter<Reply> responseStream, ServerCallContext context)
    {
        while (await requestStream.MoveNext(context.CancellationToken))
        {
            Reply terminal;
            try
            {
                terminal = await DispatchAsync(requestStream.Current, responseStream, context);
            }
            catch (Exception error) when (!context.CancellationToken.IsCancellationRequested)
            {
                terminal = EncodeFailure(error);
            }

            terminal.Complete = true;
            await responseStream.WriteAsync(terminal, context.CancellationToken);
        }
    }

    private async Task<Reply> DispatchAsync(
        Request request, IServerStreamWriter<Reply> output, ServerCallContext context)
    {
        // Re-authorize every exchange, including on sessions opened before revocation.
        _ = Authorize(context);
        switch (request.Mode)
        {
            case Mode.Describe:
                return Encode(new
                {
                    tenant = registry.GetTenant(context.RequestHeaders.GetValue(GatewayMetadata.BindingCredential)),
                    protocol = 1
                });
            case Mode.Call:
                return await CallAsync(request, context);
            case Mode.Stream:
                await StreamAsync(request, output, context);
                return new Reply();
            case Mode.Source:
                await SourceAsync(request, output, context);
                return new Reply();
            case Mode.Cancel:
                return await CancelAsync(request, context);
            default:
                throw new PluginCallException(FailureCodes.InvalidMode, false);
        }
    }

    private async Task<Reply> CallAsync(Request request, ServerCallContext context)
    {
        var client = Authorize(context);
        var result = await client.CallWithMetadataAsync(request.Operation, Decode(request), context.CancellationToken);
        Reply reply = Encode(result.Value);
        if (result.ElapsedMs is { } elapsed)
        {
            reply.ElapsedMs = elapsed;
        }

        return reply;
    }

    private async Task StreamAsync(Request request, IServerStreamWriter<Reply> output, ServerCallContext context)
    {
        string credential = context.RequestHeaders.GetValue(GatewayMetadata.BindingCredential) ?? "";
        GatewayRegistry.ActiveStream? active = null;
        try
        {
            var client = Authorize(context);
            active = registry.Begin(credential, request.StreamId, context.CancellationToken);
            CancellationToken token = active.Stop.Token;
            await WriteLiveBatchesAsync(client.StreamAsync(request.Operation, Decode(request), token), output, active);
        }
        catch (Exception error)
        {
            throw Map(error);
        }
        finally
        {
            if (active is not null)
            {
                registry.End(credential, active);
            }
        }
    }

    private async Task<Reply> CancelAsync(Request request, ServerCallContext context)
    {
        try
        {
            _ = Authorize(context);
            await registry.CancelAsync(context.RequestHeaders.GetValue(GatewayMetadata.BindingCredential)!, request.StreamId, context.CancellationToken);
            return Encode(JsonSerializer.SerializeToElement(new
            {
            }));
        }
        catch (Exception error)
        {
            throw Map(error);
        }
    }

    private IPluginClient Authorize(ServerCallContext context) => registry.Get(context.RequestHeaders.GetValue(GatewayMetadata.BindingCredential));
    private static JsonElement Decode(Request request)
    {
        if (request.Input.Length > ProtocolLimits.UnaryBytes)
        {
            throw new PluginCallException(FailureCodes.InputLimit, false);
        }

        return JsonElement.Parse(request.Input.Span);
    }

    private static Reply Encode<T>(T value) => new()
    {
        // Fresh exclusive array; never pooled, exposed for mutation or reused.
        Json = UnsafeByteOperations.UnsafeWrap(JsonSerializer.SerializeToUtf8Bytes(value))
    };
}
