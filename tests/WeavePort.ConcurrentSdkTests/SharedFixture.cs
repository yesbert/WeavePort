using System.Security.Cryptography;
using System.Text.Json;
using WeavePort.Sdk;

internal static class SharedFixture
{
    internal static Task RunAsync(bool concurrent)
    {
        var app = new PluginApplication { ConcurrentCalls = concurrent };
        app.Function<JsonElement, object>("echo", (input, context, _) => ValueTask.FromResult<object>(new { tenant = context.Tenant, value = input, pid = Environment.ProcessId }));
        app.Function<JsonElement, object>("barrier", async (input, context, token) => new { tenant = context.Tenant, callback = await context.CallHostAsync("barrier", input, token) });
        app.Function<JsonElement, object>("callbacks", async (input, context, token) =>
        {
            JsonElement result = default;
            int count = input.TryGetProperty("count", out var countValue) ? countValue.GetInt32() : 1;
            string operation = input.TryGetProperty("operation", out var operationValue) ? operationValue.GetString()! : "echo";
            for (int i = 0; i < count; i++) result = await context.CallHostAsync(operation, input, token);
            return new { tenant = context.Tenant, callback = result };
        });
        app.Function<JsonElement, string>("delay", async (input, context, _) =>
        {
            try
            {
                if (input.TryGetProperty("announce", out var announce) && announce.GetBoolean())
                {
                    await context.CallHostAsync("entered", JsonSerializer.SerializeToElement(new { }));
                }
            }
            finally
            {
                await Task.Delay(input.TryGetProperty("milliseconds", out var milliseconds) ? milliseconds.GetInt32() : 150);
            }
            return context.Tenant;
        });
        app.Function<JsonElement, object?>("fail", (_, _, _) => throw new InvalidOperationException("Author failure"));
        app.Function<JsonElement, object?>("cleanupFail", (_, context, _) =>
        {
            context.OnClose(() => throw new InvalidOperationException("Cleanup failure"));
            return ValueTask.FromResult<object?>(null);
        });
        app.Function<JsonElement, object?>("crash", async (input, _, _) =>
        {
            await Task.Delay(input.TryGetProperty("milliseconds", out var milliseconds) ? milliseconds.GetInt32() : 0);
            Environment.Exit(7);
            return null;
        });
        app.Function<JsonElement, object>("cpu", (input, context, _) =>
        {
            int iterations = input.TryGetProperty("iterations", out var value) ? value.GetInt32() : 100000;
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2("weaveport"u8, "benchmark"u8, iterations, HashAlgorithmName.SHA256, 32);
            return ValueTask.FromResult<object>(new { tenant = context.Tenant, digest = Convert.ToHexStringLower(hash) });
        });
        app.Stream<JsonElement, int>("items", (_, _, _) => Items());
        return app.RunAsync();
    }

    private static async IAsyncEnumerable<int> Items()
    {
        await Task.CompletedTask;
        yield return 1;
    }
}
