using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;
using WeavePort.Sdk.Gateway;

// Hosts, destinations and readers used by the gateway file checks.
internal static partial class FileGatewayChecks
{
    private sealed class OlderGateway : WeavePort.Sdk.Gateway.Protocol.WorkerGateway.WorkerGatewayBase
    {
        public override async Task Session(Grpc.Core.IAsyncStreamReader<WeavePort.Sdk.Gateway.Protocol.Request> requestStream, Grpc.Core.IServerStreamWriter<WeavePort.Sdk.Gateway.Protocol.Reply> responseStream, Grpc.Core.ServerCallContext context)
        {
            while (await requestStream.MoveNext(context.CancellationToken))
            {
                await responseStream.WriteAsync(new WeavePort.Sdk.Gateway.Protocol.Reply { Complete = true, Error = "invalid-mode", FailureCode = "invalid-mode", FailurePhase = "transport", MayHaveExecuted = false }, context.CancellationToken);
            }
        }
    }

    private sealed class Gateway(PluginHost host, WebApplication app, string root, string language, int blockBytes) : IAsyncDisposable
    {
        private readonly GatewayRegistry _registry = app.Services.GetRequiredService<GatewayRegistry>();
        private string _last = "";

        internal Task RevokeLastAsync() => _registry.RevokeAsync(_last);

        // Each client gets its own binding so registrations with different limits stay independent.
        internal RemotePluginClient Client(PluginFileLimits? limits, TimeSpan? hostTimeout = null)
        {
            IPluginSession session = host.BindAsync(new PluginContext("gateway-" + language, "files", "1", "test", Empty), FileChecks.Profile(root, language, blockBytes, hostTimeout), new FileChecks.NoCallbacks(), []).GetAwaiter().GetResult();
            var local = new LocalPluginClient(session);
            string credential = _last = limits is null ? _registry.Register(local) : _registry.Register(local, limits);
            return new RemotePluginClient(new Uri(app.Urls.Single()), credential, TimeSpan.FromSeconds(60));
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
            await host.DisposeAsync();
        }
    }

    private sealed class SlowReader(IPluginInputReader inner) : IPluginInputReader
    {
        public async ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken)
        {
            await Task.Delay(50, cancellationToken);
            return await inner.ReadAsync(offset, buffer, cancellationToken);
        }
    }

    private sealed class DisposalFailingDestination : IPluginOutputDestination
    {
        public ValueTask<IPluginOutputWriter> CreateAsync(string name, string mediaType, CancellationToken cancellationToken) => ValueTask.FromResult<IPluginOutputWriter>(new Writer());

        private sealed class Writer : IPluginOutputWriter
        {
            public ValueTask WriteAsync(ReadOnlyMemory<byte> block, CancellationToken cancellationToken) => ValueTask.CompletedTask;
            public ValueTask CommitAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
            public ValueTask AbortAsync(PluginOutputAbort reason, CancellationToken cancellationToken) => ValueTask.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.FromException(new IOException("dispose failed"));
        }
    }

    private sealed class FailingDestination : IPluginOutputDestination
    {
        internal List<string> Events { get; } = [];

        public ValueTask<IPluginOutputWriter> CreateAsync(string name, string mediaType, CancellationToken cancellationToken) => ValueTask.FromResult<IPluginOutputWriter>(new Writer(Events));

        private sealed class Writer(List<string> events) : IPluginOutputWriter
        {
            public ValueTask WriteAsync(ReadOnlyMemory<byte> block, CancellationToken cancellationToken) => ValueTask.CompletedTask;

            public ValueTask CommitAsync(CancellationToken cancellationToken)
            {
                events.Add("commit");
                return ValueTask.FromException(new IOException("commit failed"));
            }

            public ValueTask AbortAsync(PluginOutputAbort reason, CancellationToken cancellationToken)
            {
                events.Add("abort:" + reason);
                return ValueTask.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                events.Add("dispose");
                return ValueTask.CompletedTask;
            }
        }
    }
}
