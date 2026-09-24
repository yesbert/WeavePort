using System.Diagnostics;
using WeavePort.Abstractions;
using WeavePort.Internal;

namespace WeavePort.Hosting;

internal sealed partial class SharedWorker
{
    private static readonly ActivitySource Traces = new("WeavePort.Hosting");
    internal async Task<InvocationResult> InvokeAsync(SharedCall call)
    {
        using Activity? activity = Traces.StartActivity("plugin.invoke.shared");
        InvocationResult result = await InvokeCoreAsync(call);
        activity?.SetTag("weaveport.correlation_id", call.Id);
        if (result.Status is not (FailureCodes.Ok or FailureCodes.Cancelled or FailureCodes.Disabled))
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("error.type", result.Failure?.Code ?? result.Status);
        }

        return result;
    }
}
