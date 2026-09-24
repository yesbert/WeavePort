using System.Net.Http.Json;
using System.Text.Json;
using WeavePort.Abstractions;

internal sealed class DemoCallbacks(ExternalActionService external) : IHostCallbacks
{
    internal static readonly string[] Grants = ["documents.read", "external.perform", "external.compensate", "slow", "stubborn", "nested", "resource.use", "throw"];
    private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly SemaphoreSlim _resource = new(1);
    internal string NestedOperation { get; set; } = "echo";
    internal int MaximumResourceUsers { get; private set; }
    private int _resourceUsers;
    internal IPluginSession? Nested { get; set; }
    internal int EffectCount => external.Count;

    public async ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (call.Operation)
        {
            case "documents.read":
                if (call.Payload.TryGetProperty("tenant", out JsonElement tenant) && tenant.GetString() != call.Context.Tenant)
                {
                    throw new UnauthorizedAccessException("Foreign tenant request.");
                }

                return JsonSerializer.SerializeToElement(new[] { new { id = call.Context.Tenant + "-1", text = "WeavePort document" } });
            case "throw":
                throw new InvalidOperationException("Deliberate host callback failure");
            case "resource.use":
                return await UseResourceAsync(cancellationToken);
            case "stubborn":
                await Task.Delay(6000);
                return JsonSerializer.SerializeToElement(new
                {
                    done = true
                });
            case "slow":
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return default;
            case "external.perform":
            case "external.compensate":
                string path = call.Operation == "external.perform" ? "perform" : "compensate";
                using (HttpResponseMessage response = await Client.PostAsJsonAsync(new Uri(external.Address, path),
                    new
                    {
                        tenant = call.Context.Tenant,
                        args = call.Payload
                    }, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
                }
            case "nested":
                if (Nested is null)
                {
                    throw new InvalidOperationException("No nested binding.");
                }

                InvocationResult result = await Nested.InvokeAsync(NestedOperation, call.Payload, cancellationToken);
                return JsonSerializer.SerializeToElement(new
                {
                    result.Status,
                    result.Value,
                    call.TraceId
                });
            default:
                throw new UnauthorizedAccessException();
        }
    }
    private async Task<JsonElement> UseResourceAsync(CancellationToken cancellationToken)
    {
        await _resource.WaitAsync(cancellationToken);
        try
        {
            _resourceUsers++;
            MaximumResourceUsers = Math.Max(MaximumResourceUsers, _resourceUsers);
            await Task.Delay(150, cancellationToken);
            return JsonSerializer.SerializeToElement(new
            {
                status = "completed",
                resource = "demo-radio"
            });
        }
        finally { _resourceUsers--; _resource.Release(); }
    }

}
