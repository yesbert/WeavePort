using System.Runtime.CompilerServices;
using System.Text.Json;
using WeavePort.Sdk;

internal static class WorkerFixture
{
    private static PluginCallContext? _previous;
    private static readonly Dictionary<string, string> Cache = [];
    private static string? _file;
    private static string? _hidden;

    internal static Task RunAsync()
    {
        var app = new PluginApplication();
        app.Function<JsonElement, object>("session", SessionAsync);
        app.Function<JsonElement, object>("stash", (input, context, token) =>
        {
            _hidden = context.Tenant;
            return ValueTask.FromResult<object>(new
            {
                stored = true
            });
        });
        app.Function<JsonElement, object>("probe", (input, context, token) => ValueTask.FromResult<object>(new { hidden = _hidden }));
        app.Function<JsonElement, object>("cleanup-fail", (input, context, token) =>
        {
            context.OnClose(() => throw new IOException("Deliberate cleanup failure"));
            return ValueTask.FromResult<object>(new
            {
            });
        });
        app.Function<JsonElement, object>("cleanup-hang", (input, context, token) =>
        {
            context.OnClose(async () => await Task.Delay(30000));
            return ValueTask.FromResult<object>(new
            {
            });
        });
        app.Function<JsonElement, object>("delay", async (input, context, token) =>
        {
            await Task.Delay(input.GetProperty("ms").GetInt32(), token);
            return new
            {
                tenant = context.Tenant
            };
        });
        app.Stream<JsonElement, object>("rows", Rows);
        return app.RunAsync();
    }

    private static async ValueTask<object> SessionAsync(JsonElement input, PluginCallContext context, CancellationToken token)
    {
        bool expired = _previous is null;
        if (_previous is not null)
        {
            try
            {
                await _previous.CallHostAsync("who", JsonSerializer.SerializeToElement(new
                {
                }), token);
            }
            catch (InvalidOperationException) { expired = true; }
        }
        if (Cache.Count != 0 || _file is not null && File.Exists(_file))
        {
            throw new InvalidDataException("Registered residue");
        }

        string tenant = context.Tenant;
        string secret = context.Configuration.GetProperty("secret").GetString()!;
        Cache["secret"] = secret;
        context.OnClose(() => { Cache.Clear(); return ValueTask.CompletedTask; });
        _file = Path.Combine(Environment.GetEnvironmentVariable("WEAVEPORT_WORKSPACE") ?? Path.GetTempPath(), Guid.NewGuid() + ".session");
        string path = _file;
        context.OnClose(() => { File.Delete(path); return ValueTask.CompletedTask; });
        var file = context.Own(File.Open(path, FileMode.CreateNew));
        file.Write(System.Text.Encoding.UTF8.GetBytes(secret));
        _previous = context;
        JsonElement callback = await context.CallHostAsync("who", JsonSerializer.SerializeToElement(new
        {
            tenant = "forged"
        }), token);
        return new
        {
            tenant,
            secret,
            expired,
            callback = callback.GetString()
        };
    }

    private static async IAsyncEnumerable<object> Rows(JsonElement input, PluginCallContext context, [EnumeratorCancellation] CancellationToken token)
    {
        _previous = context;
        for (int i = 0; i < 20; i++)
        {
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            yield return new
            {
                tenant = context.Tenant,
                i
            };
        }
    }
}
