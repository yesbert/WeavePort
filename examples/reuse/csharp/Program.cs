using System.Security.Cryptography;
using WeavePort.Sdk;

var app = new PluginApplication();
app.Function<Input, Output>("transform", (input, context, cancellation) =>
{
    cancellation.ThrowIfCancellationRequested();
    // Data is local to this invocation. Never cache context, credentials or customer payloads globally.
    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(input.Text);
    context.OnClose(() =>
    {
        CryptographicOperations.ZeroMemory(bytes);
        return ValueTask.CompletedTask;
    });
    // Own transfers disposal to the SDK; registrations are unwound in reverse order.
    var buffer = context.Own(new MemoryStream());
    buffer.Write(bytes);
    return ValueTask.FromResult(new Output(context.Tenant, input.Text.ToUpperInvariant(), buffer.Length));
});
await app.RunAsync();

internal sealed record Input(string Text);
internal sealed record Output(string Tenant, string Text, long Bytes);
