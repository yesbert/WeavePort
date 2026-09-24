using System.Reflection;
using Grpc.Core;
using WeavePort.Sdk.Gateway;
using WeavePort.Sdk.Gateway.Protocol;

internal static class GatewayCancellationChecks
{
    internal static async Task RunAsync()
    {
        await VerifyAsync(cancel: true, write: false);
        await VerifyAsync(cancel: false, write: false);
        await VerifyAsync(cancel: true, write: true);
        await VerifyAsync(cancel: false, write: true);
    }

    private static async Task VerifyAsync(bool cancel, bool write)
    {
        using var stop = new CancellationTokenSource();
        var failure = new ObjectDisposedException("cancelled transport fixture");
        var reader = new DisposedReader(stop, failure, cancel);
        using var call = new AsyncDuplexStreamingCall<Request, Reply>(new DisposedWriter(stop, failure, cancel), reader,
            Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
        var method = typeof(RemotePluginClient).GetMethod(write ? "WriteAsync" : "ReadAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        Exception observed;
        try
        {
            object[] arguments = write ? [call, new Request(), stop.Token] : [call, stop.Token];
            await (Task)method.Invoke(null, arguments)!;
            throw new InvalidOperationException("Disposed read unexpectedly succeeded.");
        }
        catch (Exception error)
        {
            observed = error;
        }

        bool expected = cancel
            ? observed is OperationCanceledException cancelled && cancelled.CancellationToken == stop.Token && ReferenceEquals(cancelled.InnerException, failure)
            : ReferenceEquals(observed, failure);
        if (!expected)
        {
            throw new InvalidOperationException("Gateway disposal must follow actual cancellation ownership.", observed);
        }

        Console.WriteLine("PASS gateway transport disposal preserves cancellation ownership: " + cancel + ", write: " + write);
    }

    private sealed class DisposedWriter(CancellationTokenSource stop, Exception failure, bool cancel) : IClientStreamWriter<Request>
    {
        public WriteOptions? WriteOptions { get; set; }
        public Task CompleteAsync() => Task.CompletedTask;
        public Task WriteAsync(Request message) => WriteAsync(message, CancellationToken.None);

        public Task WriteAsync(Request message, CancellationToken cancellationToken)
        {
            if (cancel)
            {
                stop.Cancel();
            }

            throw failure;
        }
    }

    private sealed class DisposedReader(CancellationTokenSource stop, Exception failure, bool cancel) : IAsyncStreamReader<Reply>
    {
        public Reply Current => throw new InvalidOperationException("No reply in disposed fixture.");

        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (cancel)
            {
                stop.Cancel();
            }

            throw failure;
        }
    }
}
