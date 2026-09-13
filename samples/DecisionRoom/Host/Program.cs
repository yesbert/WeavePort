using System.Text.Json;
using DecisionRoom.Host;
using WeavePort.Sdk.Client;

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};
try
{
    string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException("Run through scripts/decision-room.sh; missing " + name);
    var runtime = new RuntimePaths(Required("WP_DECISION_ROOT"), Required("WP_DECISION_DOTNET"), Required("WP_DECISION_PYTHON"));
    if (args.Length == 2 && args[0] == "--activate")
    {
        runtime.Activate(args[1]);
        Console.WriteLine("New runs now default to plugin version " + args[1] + ". Existing journals retain their version.");
        return 0;
    }

    if (args.Length == 1 && args[0] == "--verify")
    {
        await Verification.RunAsync(runtime, stop.Token);
        return 0;
    }

    string configPath = "samples/DecisionRoom/config.json";
    string journalPath = Path.Combine(runtime.Root, "runs", "default.json");
    string? version = null;
    bool resume = false, restart = false, deny = false, pause = false;
    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--config" when i + 1 < args.Length:
                configPath = args[++i];
                break;
            case "--journal" when i + 1 < args.Length:
                journalPath = args[++i];
                break;
            case "--version" when i + 1 < args.Length:
                version = RuntimePaths.ValidateVersion(args[++i]);
                break;
            case "--resume":
                resume = true;
                break;
            case "--restart-after-first":
                restart = true;
                break;
            case "--deny-knowledge":
                deny = true;
                break;
            case "--pause-after-first":
                pause = true;
                break;
            default:
                throw new ArgumentException("Unknown or incomplete option. Use --version 1|2, --activate 1|2, --config PATH, --journal PATH, --resume, --restart-after-first, --deny-knowledge or --pause-after-first.");
        }
    }

    var config = JsonSerializer.Deserialize<RunConfiguration>(await File.ReadAllTextAsync(configPath, stop.Token), Wire.Json) ?? throw new InvalidDataException("Empty configuration.");
    if (version is not null)
    {
        config = config with
        {
            PluginVersion = version
        };
    }

    try
    {
        await new RoomRunner(runtime, Console.Out).RunAsync(config, new RunOptions(journalPath, resume, restart, deny, pause), stop.Token);
    }
    catch (PluginCallException error)
    {
        Console.Error.WriteLine(deny ? $"Decision unsuccessful: knowledge.read was not granted (technical status: {error.Status}). No evaluation committed for this call." : $"Decision unsuccessful: plugin status {error.Status}. Resume from committed evaluations after resolving the cause.");
        return 1;
    }

    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled. Committed evaluations remain available for resume.");
    return 130;
}
catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or JsonException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
