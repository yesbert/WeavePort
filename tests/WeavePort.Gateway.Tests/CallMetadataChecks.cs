using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WeavePort.Abstractions;
using WeavePort.Sdk.Client;
using WeavePort.Sdk.Gateway;

internal static class CallMetadataChecks
{
    internal static async Task RunAsync()
    {
        await using var registry = new GatewayRegistry();
        string credential = registry.Register(new LocalPluginClient(new TimingSession()), "timing");
        string legacyCredential = registry.Register(new LegacyClient(), "legacy");
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, endpoint => endpoint.Protocols = HttpProtocols.Http2));
        builder.Services.AddSingleton(registry);
        builder.Services.AddGrpc();
        await using var app = builder.Build();
        app.MapGrpcService<GatewayService>();
        await app.StartAsync();
        try
        {
            await using var remote = new RemotePluginClient(new Uri(app.Urls.Single()), credential);
            await using var local = new LocalPluginClient(new TimingSession());
            foreach (IPluginClient client in new IPluginClient[] { local, remote })
            {
                await VerifyCallMetadataAsync(client);
            }
            await using var legacyRemote = new RemotePluginClient(new Uri(app.Urls.Single()), legacyCredential);
            foreach (IPluginClient client in new IPluginClient[] { new LegacyClient(), legacyRemote })
            {
                var result = await client.CallWithMetadataAsync<int, int>("echo", 42);
                if (result.Value != 42 || result.ElapsedMs is not null)
                {
                    throw new Exception("Legacy timing fabricated.");
                }
            }
        }
        finally { await app.StopAsync(); }
        Console.WriteLine("PASS: concurrent local/gateway per-call timing, legacy unavailable timing and version diagnostics");
    }

    private static async Task VerifyCallMetadataAsync(IPluginClient client)
    {
        var results = await Task.WhenAll(Enumerable.Range(1, 8).Select(value => client.CallWithMetadataAsync<int, int>("echo", value)));
        for (int index = 0; index < results.Length; index++)
        {
            if (results[index].Value != index + 1 || results[index].ElapsedMs != (index + 1) * 10.5)
            {
                throw new Exception("Per-call metadata changed.");
            }
        }

        try
        {
            await client.CallWithMetadataAsync<int, int>("failure", 0);
            throw new Exception("Failure accepted");
        }
        catch (PluginCallException error) when (error.Status == "failed" && error.MayHaveExecuted && error.Failure == new PluginFailure("sdk-error", "exchange", "correlation-fixture", true)) { }
        try
        {
            await client.CallWithMetadataAsync<int, int>("mismatch", 0);
            throw new Exception("Mismatch accepted");
        }
        catch (PluginCallException error) when (error.Status == "version-mismatch" && !error.MayHaveExecuted && error.VersionMismatch == new PluginVersionMismatch("1.0.0", "1")) { }
    }

    private sealed class TimingSession : IPluginOperationSession
    {
        public string Tenant => "timing";
        public string Instance => "fixture";
        public bool SupportsStreaming => false;
        public ValueTask<IPluginSession> AcquireOperationAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async Task<InvocationResult> InvokeAsync(string operation, JsonElement payload, CancellationToken cancellationToken = default)
        {
            if (payload.GetProperty("operation").GetString() == "mismatch")
            {
                return new("version-mismatch", JsonSerializer.SerializeToElement(new
                {
                }), Instance, 1)
                {
                    VersionMismatch = new("1.0.0", "1")
                };
            }

            if (payload.GetProperty("operation").GetString() == "failure")
            {
                return new("failed", JsonSerializer.SerializeToElement(new
                {
                }), Instance, 1, true)
                {
                    Failure = new("sdk-error", "exchange", "correlation-fixture", true)
                };
            }

            int value = payload.GetProperty("input").GetInt32();
            await Task.Delay((9 - value) * 5, cancellationToken);
            return new("ok", JsonSerializer.SerializeToElement(value), Instance, value * 10.5, true);
        }
        public Task RestartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class LegacyClient : IPluginClient
    {
        public Task<JsonElement> CallAsync(string operation, JsonElement input, CancellationToken cancellationToken = default) => Task.FromResult(input);
        public IAsyncEnumerable<JsonElement> StreamAsync(string operation, JsonElement input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
