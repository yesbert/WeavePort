using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using WeavePort.Sdk.Gateway;

// Process-owned fixture. The known-stalled control is terminated after the bounded stop attempt.
internal sealed class RegressionHost
{
    internal ConcurrentQueue<object> Requests { get; } = new();
    internal GatewayRegistry Registry { get; } = new();
    internal WebApplication App { get; }
    internal Uri Address { get; private set; } = null!;
    internal RemotePluginClient Sdk { get; private set; } = null!;
    private HttpClient _http = null!;
    private readonly string _credential;

    internal RegressionHost()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(1));
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, endpoint => endpoint.Protocols = HttpProtocols.Http2));
        _credential = Registry.Register(new EchoClient(), "test");
        builder.Services.AddSingleton(Registry);
        builder.Services.AddGrpc();
        App = builder.Build();
        App.Use(async (context, next) =>
        {
            object stream = context.Features.Get<IHttpResponseFeature>()!;
            Requests.Enqueue(KestrelState.Field(stream, "_http2Output"));
            await next(context);
        });
        App.MapGrpcService<GatewayService>();
        App.MapGet("/control", () => "ok");
    }

    internal async Task StartAsync()
    {
        await App.StartAsync();
        Address = new Uri(App.Urls.Single());
        _http = new HttpClient(new SocketsHttpHandler())
        {
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Timeout = TimeSpan.FromSeconds(1)
        };
        Sdk = new RemotePluginClient(Address, _credential, TimeSpan.FromSeconds(1));
    }

    internal async Task CallAsync(bool control)
    {
        if (control)
        {
            await CallControlAsync();
            return;
        }
        JsonElement result = await Sdk.CallAsync("echo", JsonSerializer.SerializeToElement(new
        {
            value = "ok"
        }));
        if (result.GetProperty("value").GetString() != "ok")
        {
            throw new InvalidDataException("Gateway echo changed the value.");
        }
    }

    private async Task CallControlAsync()
    {
        if (await _http.GetStringAsync(new Uri(Address, "/control")) != "ok")
        {
            throw new InvalidDataException("Runtime control changed the value.");
        }
    }

    internal async Task StopAsync()
    {
        try
        {
            await App.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (TimeoutException)
        {
            Console.WriteLine("Known stalled control required process cleanup.");
        }
    }
}
