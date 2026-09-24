using System.Diagnostics;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;

internal sealed class RetainedCallbacks : IHostCallbacks
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken token)
    {
        if (call.Operation == "block")
        {
            Entered.TrySetResult();
            await Release.Task;
        }
        return JsonSerializer.SerializeToElement(new
        {
            tenant = call.Context.Tenant
        });
    }
}
