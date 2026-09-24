using System.Text.Json;

namespace WeavePort.Hosting;
/// <summary>Authenticated tenant identity and configuration supplied by the application.</summary>
/// <param name = "Tenant">Trusted data owner.</param>
/// <param name = "Configuration">Binding configuration; never shared between tenant-bound workers.</param>
public sealed record TenantBinding(string Tenant, JsonElement Configuration);
