using WeavePort.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace WeavePort.Sdk.Gateway;
/// <summary>Registers the bounded gateway transport; the application owns listener security and binding registration.</summary>
public static class GatewayHostingExtensions
{
    /// <summary>Adds an application-owned registry and service-specific one-MiB gRPC message limits.</summary>
    public static IServiceCollection AddWeavePortGateway(this IServiceCollection services)
    {
        services.TryAddSingleton<GatewayRegistry>();
        services.AddGrpc().AddServiceOptions<GatewayService>(options =>
        {
            options.MaxReceiveMessageSize = ProtocolLimits.FrameBytes;
            options.MaxSendMessageSize = ProtocolLimits.FrameBytes;
        });
        return services;
    }

    /// <summary>Maps the gateway service. Configure HTTPS/HTTP2 before accepting remote connections.</summary>
    public static IEndpointConventionBuilder MapWeavePortGateway(this IEndpointRouteBuilder endpoints) => endpoints.MapGrpcService<GatewayService>();
}
