using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using WeavePort.Sdk.Gateway;

internal sealed class TlsFixture : IAsyncDisposable
{
    private readonly RSA _key = RSA.Create(2048);
    internal X509Certificate2 Root { get; }
    internal X509Certificate2 Certificate { get; }
    internal WebApplication App { get; private set; } = null!;
    internal Uri Address { get; private set; } = null!;
    internal GatewayRegistry Registry { get; private set; } = null!;

    internal TlsFixture()
    {
        var request = new CertificateRequest("CN=WeavePort test CA", _key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        Root = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        var server = new CertificateRequest("CN=localhost", _key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        server.CertificateExtensions.Add(names.Build());
        server.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        server.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
        using var signed = server.Create(Root, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(50), RandomNumberGenerator.GetBytes(16));
        Certificate = signed.CopyWithPrivateKey(_key);
    }

    internal async Task StartAsync(int port = 0, bool incompatible = false)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(1));
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, port, e => { e.Protocols = HttpProtocols.Http2; e.UseHttps(Certificate); }));
        builder.Services.AddWeavePortGateway();
        App = builder.Build();
        Registry = App.Services.GetRequiredService<GatewayRegistry>();
        if (incompatible) App.MapGrpcService<IncompatibleGateway>();
        else App.MapWeavePortGateway();
        await App.StartAsync();
        Address = new UriBuilder(App.Urls.Single()) { Host = "localhost" }.Uri;
    }

    internal RemotePluginClient Client(string credential, Uri? address = null, bool trust = true)
    {
        var handler = new SocketsHttpHandler();
        if (trust)
        {
            // Use normal chain/hostname checks with a private test trust root;
            // do not install a system certificate or accept arbitrary certificates.
            handler.SslOptions.CertificateChainPolicy = new X509ChainPolicy
            {
                TrustMode = X509ChainTrustMode.CustomRootTrust,
                RevocationMode = X509RevocationMode.NoCheck
            };
            handler.SslOptions.CertificateChainPolicy.CustomTrustStore.Add(Root);
        }
        return new RemotePluginClient(address ?? Address, credential,
            new GrpcChannelOptions { HttpHandler = handler, DisposeHttpClient = true }, TimeSpan.FromSeconds(3));
    }

    internal async Task StopAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
    }
    public async ValueTask DisposeAsync()
    {
        if (App is not null) await StopAsync();
        Certificate.Dispose(); Root.Dispose(); _key.Dispose();
    }
}
