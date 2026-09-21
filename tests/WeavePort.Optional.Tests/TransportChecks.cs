using Grpc.Net.Client;
using WeavePort.Sdk.Gateway;

internal static class TransportChecks
{
    internal static async Task RunAsync()
    {
        foreach (bool owned in new[] { false, true })
        {
            using var handler = new PendingHandler();
            var client = new RemotePluginClient(new Uri("https://localhost"), "test",
                new GrpcChannelOptions { HttpHandler = handler, DisposeHttpClient = owned }, callTimeout: null);
            var pending = client.GetTenantAsync();
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await client.DisposeAsync();
            await Check.Fails<OperationCanceledException>(() => pending, "client disposal cancels active HTTP request");
            await handler.Ended.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check.That(handler.Disposals == (owned ? 1 : 0), "HTTP handler ownership respected: " + owned);
            await client.DisposeAsync();
            Check.That(handler.Disposals == (owned ? 1 : 0), "repeated disposal does not dispose handler twice");
            await Check.Fails<OperationCanceledException>(() => client.GetTenantAsync(), "disposed client rejects discovery");
        }
    }

    private sealed class PendingHandler : HttpMessageHandler
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Ended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Disposals { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { Ended.TrySetResult(); }
            throw new InvalidOperationException("Unreachable");
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) Disposals++;
            base.Dispose(disposing);
        }
    }
}
