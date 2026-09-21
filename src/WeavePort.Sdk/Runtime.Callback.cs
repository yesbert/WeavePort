namespace WeavePort.Sdk;
internal sealed partial class Runtime
{
    private async Task WaitForCallbackExchangeAsync(CallScope scope, CancellationToken token)
    {
        while (true)
        {
            await scope.Ready.Task.WaitAsync(token);
            await _callbackGate.WaitAsync(token);
            if (!scope.Active || scope.Request.ValueKind != System.Text.Json.JsonValueKind.Undefined)
            {
                return;
            }

            _callbackGate.Release();
        }
    }
}
