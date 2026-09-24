using System.Text.Json;
using WeavePort.Hosting;

// Only the host's existing safe source-generated events; no exception objects or payloads.
internal sealed class FixtureLogger : ILogger<PluginHost>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => level >= LogLevel.Warning;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
    {
        if (id.Id is >= 1001 and <= 1006)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                at = TimeProvider.System.GetUtcNow(),
                eventId = id.Id,
                message = formatter(state, null)
            }));
        }
    }
}
