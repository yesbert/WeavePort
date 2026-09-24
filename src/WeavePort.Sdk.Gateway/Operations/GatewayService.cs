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
                Request request = requestStream.Current;
                // Re-authorize every exchange, including on sessions opened before revocation.
                _ = Authorize(context);
                switch (request.Mode)
                {
                    case Mode.Describe:
                        terminal = Encode(new { tenant = registry.GetTenant(context.RequestHeaders.GetValue(GatewayMetadata.BindingCredential)), protocol = 1 });
                        break;
                    case Mode.Call:
                        terminal = await CallAsync(request, context);
                        break;
                    case Mode.Stream:
                        await StreamAsync(request, responseStream, context);
                        terminal = new Reply();
                        break;
                    case Mode.Source:
                        await SourceAsync(request, responseStream, context);
                        terminal = new Reply();
                        break;
                    case Mode.Cancel:
                        terminal = await CancelAsync(request, context);
                        break;
                    default:
                        throw new PluginCallException(FailureCodes.InvalidMode, false);
                }
            }
            catch (Exception error) when (!context.CancellationToken.IsCancellationRequested)
            {
                RpcException mapped = error as RpcException ?? Map(error);
                terminal = new Reply
                {
                    Error = error is PluginCallException legacy ? FailureCode.Normalize(legacy.Status) : FailureCode.Normalize(mapped.Trailers.GetValue(GatewayMetadata.FailureCode)),
                    FailureCode = FailureCode.Normalize(mapped.Trailers.GetValue(GatewayMetadata.FailureCode)),
                    FailurePhase = mapped.Trailers.GetValue(GatewayMetadata.FailurePhase) ?? FailurePhases.Transport,
                    CorrelationId = mapped.Trailers.GetValue(GatewayMetadata.CorrelationId) ?? "",
                    CleanupFailed = mapped.Trailers.GetValue(GatewayMetadata.CleanupFailed) == "true",
                    MayHaveExecuted = mapped.StatusCode != StatusCode.Unauthenticated && mapped.Trailers.GetValue(GatewayMetadata.MayHaveExecuted) != "false",
                    Cancelled = mapped.StatusCode == StatusCode.Cancelled
                };
                IncludeVersionMismatch(terminal, error);
            }

            terminal.Complete = true;
            await responseStream.WriteAsync(terminal, context.CancellationToken);
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
            return Encode(JsonSerializer.SerializeToElement(new { }));
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
    private static RpcException Map(Exception error)
    {
        if (error is RpcException rpc)
        {
            return rpc;
        }

        var call = error as PluginCallException;
        string code = error switch
        {
            UnauthorizedAccessException => FailureCodes.BindingDenied,
            OperationCanceledException => FailureCodes.Cancelled,
            PluginCallException => FailureCode.Normalize(call!.Failure?.Code ?? call.Status),
            _ => FailureCodes.GatewayFailed
        };
        StatusCode status = error switch
        {
            UnauthorizedAccessException => StatusCode.Unauthenticated,
            OperationCanceledException => StatusCode.Cancelled,
            PluginCallException => StatusCode.FailedPrecondition,
            _ => StatusCode.Internal
        };
        var trailers = new Metadata
        {
            {
                GatewayMetadata.FailureCode,
                code
            },
            {
                GatewayMetadata.FailurePhase,
                call?.Failure?.Phase ?? FailurePhases.Transport
            },
            {
                GatewayMetadata.CorrelationId,
                call?.Failure?.CorrelationId ?? ""
            },
            {
                GatewayMetadata.CleanupFailed,
                call?.Failure?.CleanupFailed == true ? "true" : "false"
            },
            {
                GatewayMetadata.MayHaveExecuted,
                call?.MayHaveExecuted == false || error is UnauthorizedAccessException ? "false" : "true"
            }
        };
        return new RpcException(new Status(status, code), trailers);
    }

    private static void IncludeVersionMismatch(Reply terminal, Exception error)
    {
        if (error is not PluginCallException { VersionMismatch: { } mismatch })
        {
            return;
        }

        terminal.ExpectedVersion = mismatch.Expected;
        terminal.AdvertisedVersion = mismatch.Advertised;
    }
}
