using WeavePort.Abstractions;
using WeavePort.Internal;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using WeavePort.Sdk.Client;
using WeavePort.Sdk.Gateway.Protocol;

namespace WeavePort.Sdk.Gateway;
/// <summary>Product client for a preconfigured worker-host binding; the plugin artifact stays unchanged.</summary>
public sealed partial class RemotePluginClient : IBoundPluginClient
{
    private readonly GatewaySessions _sessions;
    private readonly TimeSpan _callTimeout;
    private readonly PluginStreamOptions _streamOptions;
    /// <summary>Creates a binding client with at most eight leased sessions and a default 30-second unary timeout, including lease wait. Streams use separate exchange and total deadlines. Plain HTTP is permitted only on loopback; remote endpoints require HTTPS.</summary>
    public RemotePluginClient(Uri endpoint, string bindingCredential, TimeSpan? callTimeout = null, PluginStreamOptions? streamOptions = null) : this(endpoint, bindingCredential, new GrpcChannelOptions(), callTimeout, streamOptions)
    {
    }

    /// <summary>Creates a client with transport configuration for trusted certificate/handler policy. Handler ownership follows DisposeHttpClient; transport message limits are enforced by this library.</summary>
    public RemotePluginClient(Uri endpoint, string bindingCredential, GrpcChannelOptions channelOptions, TimeSpan? callTimeout, PluginStreamOptions? streamOptions = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(channelOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingCredential);
        if (endpoint.Scheme != "https" && !(endpoint.Scheme == "http" && endpoint.IsLoopback))
        {
            throw new ArgumentException("Remote gateway requires HTTPS.", nameof(endpoint));
        }

        _callTimeout = callTimeout ?? TimeSpan.FromSeconds(30);
        if (!IsSupportedTimeout(_callTimeout))
        {
            throw new ArgumentOutOfRangeException(nameof(callTimeout));
        }

        _streamOptions = streamOptions ?? new PluginStreamOptions();
        ValidateStreamOptions(_streamOptions);
        _sessions = new(endpoint, new() { { GatewayMetadata.BindingCredential, bindingCredential } }, channelOptions);
    }

    /// <inheritdoc/>
    public async Task<string> GetTenantAsync(CancellationToken cancellationToken = default)
    {
        JsonElement identity = await ExchangeAsync(new Request { Mode = Mode.Describe }, cancellationToken);
        if (identity.ValueKind != JsonValueKind.Object || !identity.TryGetProperty(WireFields.Protocol, out JsonElement protocol) || protocol.ValueKind != JsonValueKind.Number || !protocol.TryGetInt32(out int version) || version != 1)
        {
            throw new InvalidDataException("Incompatible gateway binding identity.");
        }

        if (!identity.TryGetProperty(WireFields.Tenant, out JsonElement tenant) || tenant.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(tenant.GetString()))
        {
            throw new InvalidDataException("Incompatible gateway binding identity.");
        }

        return tenant.GetString()!;
    }

    /// <inheritdoc/>
    public Task<JsonElement> CallAsync(string operation, JsonElement input, CancellationToken cancellationToken = default) => ExchangeAsync(Request(operation, input), cancellationToken);
    /// <inheritdoc/>
    public Task<PluginCallResult<JsonElement>> CallWithMetadataAsync(string operation, JsonElement input, CancellationToken cancellationToken = default) => ExchangeResultAsync(Request(operation, input), cancellationToken);
    private async Task<JsonElement> ExchangeAsync(Request request, CancellationToken cancellationToken, bool cleanup = false) => (await ExchangeResultAsync(request, cancellationToken, cleanup)).Value;
    private async Task<PluginCallResult<JsonElement>> ExchangeResultAsync(Request request, CancellationToken cancellationToken, bool cleanup = false)
    {
        using var stop = cleanup ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken) : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _sessions.Lifetime);
        stop.CancelAfter(cleanup ? _streamOptions.ExchangeTimeout : _callTimeout);
        var session = await _sessions.RentAsync(stop.Token);
        bool complete = false;
        try
        {
            await session.RequestStream.WriteAsync(request, stop.Token);
            Reply reply = await ReadStreamReplyAsync(session, stop.Token);
            complete = reply.Complete;
            if (!complete)
            {
                throw new InvalidDataException("Missing gateway terminal reply.");
            }

            CheckError(reply, stop.Token);
            if (reply.Json.Length > ProtocolLimits.UnaryBytes)
            {
                throw new PluginCallException(FailureCodes.ValueLimit);
            }

            if (reply.HasElapsedMs && (!double.IsFinite(reply.ElapsedMs) || reply.ElapsedMs < 0))
            {
                throw new InvalidDataException("Invalid gateway elapsed timing.");
            }

            return new(JsonElement.Parse(reply.Json.Span), reply.HasElapsedMs ? reply.ElapsedMs : null);
        }
        catch (RpcException error)
        {
            throw Translate(error, stop.Token);
        }
        finally
        {
            _sessions.Return(session, complete);
        }
    }

    private static async Task<Reply> ReadAsync(AsyncDuplexStreamingCall<Request, Reply> session, CancellationToken token)
    {
        if (!await session.ResponseStream.MoveNext(token))
        {
            throw new IOException("Gateway session ended without a terminal reply.");
        }

        return session.ResponseStream.Current;
    }

    private static void CheckError(Reply reply, CancellationToken token)
    {
        if (reply.Cancelled)
        {
            throw new OperationCanceledException("Gateway call cancelled.", token);
        }

        if (reply.Error.Length > 0)
        {
            throw new PluginCallException(FailureCode.Normalize(reply.Error), reply.MayHaveExecuted)
            {
                Failure = new PluginFailure(FailureCode.Normalize(reply.FailureCode.Length == 0 ? reply.Error : reply.FailureCode), FailureCode.Phase(reply.FailurePhase), FailureCode.Correlation(reply.CorrelationId), reply.CleanupFailed),
                VersionMismatch = reply.HasExpectedVersion && reply.HasAdvertisedVersion ? new PluginVersionMismatch(reply.ExpectedVersion, reply.AdvertisedVersion) : null
            };
        }
    }

    private static Request Request(string operation, JsonElement input)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(input);
        if (json.Length > ProtocolLimits.UnaryBytes)
        {
            throw new PluginCallException(FailureCodes.InputLimit, false);
        }

        return new()
        {
            Operation = operation,
            // Transfer the fresh serializer array; no mutable alias survives this method.
            Input = UnsafeByteOperations.UnsafeWrap(json)
        };
    }

    /// <summary>Cancels outstanding calls and disposes binding-scoped transport sessions; server binding lifetime remains owned by the trusted worker-host application.</summary>
    public ValueTask DisposeAsync()
    {
        _sessions.Dispose();
        return ValueTask.CompletedTask;
    }
}
