using System.Runtime.CompilerServices;
using System.Text.Json;
using WeavePort.Sdk.Client;

internal static class GatewayChecks
{
    internal static async Task RunAsync(TlsFixture tls)
    {
        var fixture = new ControlledClient();
        string token = tls.Registry.Register(fixture, "controlled");
        await using var client = tls.Client(token);
        Check.That(await client.GetTenantAsync() == "controlled" && fixture.Calls == 0, "discovery never invokes plugin code");
        using (var stop = new CancellationTokenSource())
        {
            var pending = client.CallAsync("wait", Check.Json(0), stop.Token);
            await fixture.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            stop.Cancel();
            await Check.Fails<OperationCanceledException>(() => pending, "TLS caller cancellation");
            await fixture.Ended.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check.That((await client.CallAsync("echo", Check.Json(7))).GetInt32() == 7, "TLS call after cancellation");
        }
        var stream = client.StreamAsync("wait", Check.Json(0)).GetAsyncEnumerator();
        Check.That(await stream.MoveNextAsync(), "active TLS stream delivers a batch");
        await tls.Registry.RevokeAsync(token);
        await Check.Fails<OperationCanceledException>(async () =>
        {
            while (await stream.MoveNextAsync())
            {
            }
        }, "active TLS stream cancelled by revocation");
        try
        {
            await stream.DisposeAsync();
        }
        catch (PluginCallException) { }

        fixture = new ControlledClient();
        token = tls.Registry.Register(fixture, "restart");
        await using var reconnecting = tls.Client(token);
        int port = tls.Address.Port;
        var interrupted = reconnecting.CallAsync("wait", Check.Json(0));
        await fixture.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await tls.StopAsync();
        // A connection loss may race its deadline; neither is a successful result.
        bool failed = false;
        try
        {
            await interrupted;
        }
        catch (Exception error) when (error is PluginCallException or OperationCanceledException or IOException)
        {
            failed = true;
        }
        Check.That(failed, "interrupted TLS call has observable failure");
        Check.That(fixture.Calls == 1, "interrupted operation is not replayed");
        await tls.StartAsync(port);
        // The new registry does not revive old credentials.
        await Check.Fails<PluginCallException>(() => reconnecting.GetTenantAsync(), "reconnected old credential denied after server restart");
        string fresh = tls.Registry.Register(new ControlledClient(), "restart");
        await using var restarted = tls.Client(fresh);
        Check.That((await restarted.CallAsync("echo", Check.Json(9))).GetInt32() == 9, "fresh binding works after HTTPS server restart");
    }

    private sealed class ControlledClient : IPluginClient
    {
        private readonly CancellationTokenSource _stop = new();
        internal int Calls { get; private set; }
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Ended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<JsonElement> CallAsync(string operation, JsonElement input, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (operation == "wait")
            {
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
                Started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, stop.Token);
                }
                finally { Ended.TrySetResult(); }
            }
            return input;
        }
        public async IAsyncEnumerable<JsonElement> StreamAsync(string operation, JsonElement input, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
            for (int i = 0; i < 17; i++)
            {
                yield return Check.Json(i);
            }

            await Task.Delay(Timeout.Infinite, stop.Token);
        }
        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
        }
    }
}
