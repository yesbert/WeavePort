using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using WeavePort.Sdk.Client;
using WeavePort.Sdk.Gateway;

// Reflection is confined to this version-specific regression. It forces the late
// Schedule ordering that explains the captured blocked state; production never edits Kestrel state.
string output = args[0];
bool control = args.Contains("--runtime-control");
var requests = new ConcurrentQueue<object>();
var builder = WebApplication.CreateSlimBuilder();
builder.Logging.ClearProviders();
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(1));
builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, endpoint => endpoint.Protocols = HttpProtocols.Http2));
var registry = new GatewayRegistry();
string credential = registry.Register(new EchoClient(), "test");
builder.Services.AddSingleton(registry);
builder.Services.AddGrpc();
var app = builder.Build();
app.Use(async (context, next) =>
{
    object stream = context.Features.Get<IHttpResponseFeature>()!;
    requests.Enqueue(Field(stream, "_http2Output"));
    await next(context);
});
app.MapGrpcService<GatewayService>();
app.MapGet("/control", () => "ok");
await app.StartAsync();
var address = new Uri(app.Urls.Single());
using var http = new HttpClient(new SocketsHttpHandler()) { DefaultRequestVersion = HttpVersion.Version20, DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact, Timeout = TimeSpan.FromSeconds(1) };
await using var sdk = new RemotePluginClient(address, credential, TimeSpan.FromSeconds(1));
async Task CallAsync()
{
    if (control)
    {
        if (await http.GetStringAsync(new Uri(address, "/control")) != "ok") throw new InvalidDataException();
    }
    else
    {
        JsonElement result = await sdk.CallAsync("echo", JsonSerializer.SerializeToElement(new { value = "ok" }));
        if (result.GetProperty("value").GetString() != "ok") throw new InvalidDataException();
    }
}
await CallAsync();
if (!requests.TryPeek(out object? producer)) throw new InvalidOperationException("Missing stream capture.");
if (control) await UntilAsync(() => (bool)Field(producer, "_completedResponse"));
bool responseCompletedBeforeSchedule = (bool)Field(producer, "_completedResponse");
object writer = Field(producer, "_frameWriter");
// A window-update continuation can pass its completed-response check before the
// response finishes and call Schedule after completion. Execute exactly that tail.
producer.GetType().GetMethod("Schedule")!.Invoke(producer, null);
await UntilAsync(() => QueueCount(writer) == 0);
bool staleScheduled = (bool)Field(producer, "_isScheduled");
bool writerCompleted = (bool)Field(writer, "_completed");
string? failure = null;
try { await CallAsync(); }
catch (OperationCanceledException) { failure = "cancelled"; }
catch (IOException) { failure = "io-error"; }
object[] captured = requests.ToArray();
bool reused = captured.Length >= 2 && ReferenceEquals(captured[0], captured[1]);
bool persistentSession = !control && captured.Length == 1 && !responseCompletedBeforeSchedule;
bool disposedCallCancelled = false;
object? sessionChecks = null;
if (!control && failure is null)
{
    sessionChecks = await SessionChecks.RunAsync(sdk, registry, address, requests);
    Task pending = ConsumeWaitingAsync(sdk);
    await Task.Delay(30);
    await sdk.DisposeAsync();
    try { await pending.WaitAsync(TimeSpan.FromSeconds(7)); }
    catch (OperationCanceledException) { disposedCallCancelled = true; }
    if (!disposedCallCancelled) throw new InvalidOperationException("Disposed client did not cancel its active stream.");
}
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
{
    control, runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    sdkHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(RemotePluginClient).Assembly.Location))),
    lateSchedule = true, responseCompletedBeforeSchedule, persistentSession, staleScheduled, writerCompleted, queueCount = QueueCount(writer),
    sameProducerReused = reused, requestCount = captured.Length, failure, disposedCallCancelled, sessionChecks,
    passed = control ? failure == "cancelled" && staleScheduled && reused : failure is null && persistentSession
}));
Console.WriteLine($"control={control} staleScheduled={staleScheduled} reused={reused} failure={failure ?? "none"}");
try { await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
catch (TimeoutException) { Console.WriteLine("Known stalled control required process cleanup."); }
Environment.Exit(control ? (failure == "cancelled" && staleScheduled && reused ? 0 : 1) : failure is null && persistentSession ? 0 : 1);

static async Task ConsumeWaitingAsync(IPluginClient client)
{
    await foreach (var item in client.StreamAsync("wait", JsonSerializer.SerializeToElement(new { }))) { }
}
static object Field(object owner, string name)
{
    for (Type? type = owner.GetType(); type is not null; type = type.BaseType)
        if (type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) is { } field)
            return field.GetValue(owner)!;
    throw new MissingFieldException(owner.GetType().FullName, name);
}
static int QueueCount(object writer) => (int)Field(Field(Field(writer, "_channel"), "_items"), "_size");
static async Task UntilAsync(Func<bool> condition)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    while (!condition()) await Task.Delay(1, timeout.Token);
}
internal sealed class EchoClient : IPluginClient
{
    public async Task<JsonElement> CallAsync(string operation, JsonElement input, CancellationToken cancellationToken = default)
    {
        if (operation == "fail") throw new PluginCallException("synthetic-failure", false);
        if (operation == "parallel") await Task.Delay(25, cancellationToken);
        return input;
    }
    public async IAsyncEnumerable<JsonElement> StreamAsync(string operation, JsonElement input, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (operation == "wait") await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
        yield return input;
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
