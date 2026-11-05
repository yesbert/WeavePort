using System.Text.Json;
using Grpc.Core;
using WeavePort.Abstractions;
using WeavePort.Internal;
using WeavePort.Sdk.Client;
using WeavePort.Sdk.Gateway.Protocol;

namespace WeavePort.Sdk.Gateway;

public sealed partial class RemotePluginClient
{
    /// <summary>Invokes a function with named inputs and outputs whose readers and writers stay in this process. The gateway relays each block over the session; its registration caps the limits. Large files need a call timeout that covers the transfer.</summary>
    public async Task<PluginCallResult<JsonElement>> CallWithFilesAsync(string operation, JsonElement input, PluginFiles files, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        Request request = FileRequest(operation, input, files);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _sessions.Lifetime);
        stop.CancelAfter(_callTimeout);
        var session = await _sessions.RentAsync(stop.Token);
        var caller = new CallerFiles(files);
        bool complete = false;
        try
        {
            await WriteAsync(session, request, stop.Token);
            Reply reply = await RelayUntilTerminalAsync(session, caller, stop.Token);
            complete = true;
            return Result(reply, caller, stop.Token);
        }
        catch (RpcException error)
        {
            throw Translate(error, stop.Token);
        }
        finally
        {
            await caller.ReleaseAsync(stop.IsCancellationRequested);
            _sessions.Return(session, complete);
        }
    }

    private static async Task<Reply> RelayUntilTerminalAsync(AsyncDuplexStreamingCall<Request, Reply> session, CallerFiles caller, CancellationToken token)
    {
        while (true)
        {
            Reply reply = await ReadAsync(session, token);
            if (reply.Complete)
            {
                return reply;
            }

            FileRequest fileRequest = reply.FileRequest ?? throw new InvalidDataException("Unexpected gateway reply during a file call.");
            await WriteAsync(session, new Request { FileAck = await caller.HandleAsync(fileRequest, token) }, token);
        }
    }

    private static PluginCallResult<JsonElement> Result(Reply reply, CallerFiles caller, CancellationToken token)
    {
        try
        {
            CheckError(reply, token);
        }
        catch (PluginCallException error)
        {
            // A gateway older than function files rejects FILE_CALL as an unknown mode before dispatch.
            string status = error.Status == FailureCodes.InvalidMode ? FailureCodes.FilesUnsupported : error.Status;
            throw new PluginCallException(status, error.MayHaveExecuted)
            {
                Failure = error.Failure,
                VersionMismatch = error.VersionMismatch,
                Outputs = caller.Committed
            };
        }

        if (reply.Json.Length > ProtocolLimits.UnaryBytes)
        {
            throw new PluginCallException(FailureCodes.ValueLimit);
        }

        if (reply.HasElapsedMs && (!double.IsFinite(reply.ElapsedMs) || reply.ElapsedMs < 0))
        {
            throw new InvalidDataException("Invalid gateway elapsed timing.");
        }

        return new(JsonElement.Parse(reply.Json.Span), reply.HasElapsedMs ? reply.ElapsedMs : null)
        {
            Outputs = caller.Committed
        };
    }

    private static Request FileRequest(string operation, JsonElement input, PluginFiles files)
    {
        Request request = Request(operation, input);
        request.Mode = Mode.FileCall;
        request.Inputs.Add(files.Inputs.Select(item => new FileInput { Name = item.Name, Length = item.Length, MediaType = item.MediaType }));
        request.FileLimits = new FileLimits
        {
            Outputs = files.Outputs is not null,
            MaximumOutputs = files.Limits.MaximumOutputs,
            MaximumOutputBytes = files.Limits.MaximumOutputBytes
        };
        if (files.Limits.MaximumInputReadBytes is { } budget)
        {
            request.FileLimits.MaximumInputReadBytes = budget;
        }

        return request;
    }
}
