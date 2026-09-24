using System.Text.Json;
using Microsoft.Extensions.Logging;
using WeavePort.Abstractions;
using WeavePort.Hosting;

internal static class DiagnosticsChecks
{
    public static async Task RunAsync(string workspace)
    {
        var logger = new BlockingLogger();
        try
        {
            await using var host = new PluginHost(logger, options: new WorkerPoolOptions(MaximumPristineWorkers: 0));
            var profile = new ProcessProfile(Environment.ProcessPath!, [typeof(DiagnosticsChecks).Assembly.Location, "--worker", "--stderr-flood"],
                true, workspace, reservedMemoryMiB: 64);
            var context = new PluginContext("diagnostics", "fixture", "1", "test", JsonSerializer.SerializeToElement(new
            {
            }));
            await using (var quiet = await host.BindAsync(context, profile, new NoCallbacks(), []))
            {
                if ((await quiet.InvokeAsync("echo", context.Configuration)).Status != "ok" || logger.Lines != 0)
                {
                    throw new InvalidOperationException("stderr must remain silent by default.");
                }
            }
            await using var enabled = await host.BindAsync(context, profile with
            {
                ForwardStandardError = true
            }, new NoCallbacks(), []);
            if ((await enabled.InvokeAsync("echo", context.Configuration)).Status != "ok")
            {
                throw new InvalidOperationException("Blocked logger stalled worker stdout.");
            }

            await logger.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (logger.MaximumLength > 4096)
            {
                throw new InvalidOperationException("stderr line exceeded bound.");
            }
            // Host disposal must not wait for arbitrary synchronous user logger code.
            await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Console.WriteLine("PASS bounded opt-in stderr, flood draining and nonblocking shutdown");
        }
        finally { logger.Release.Set(); }
    }

    private sealed class BlockingLogger : ILogger<PluginHost>
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Release = new();
        public int Lines;
        public int MaximumLength;
        public bool IsEnabled(LogLevel level) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? error, Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id != 1007)
            {
                return;
            }

            Interlocked.Increment(ref Lines);
            if (state is IEnumerable<KeyValuePair<string, object?>> fields)
            {
                string? line = fields.FirstOrDefault(field => field.Key == "DiagnosticLine").Value as string;
                MaximumLength = Math.Max(MaximumLength, line?.Length ?? 0);
            }
            Entered.TrySetResult();
            Release.Wait();
        }
    }

    private sealed class NoCallbacks : IHostCallbacks
    {
        public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken) => throw new UnauthorizedAccessException();
    }
}
