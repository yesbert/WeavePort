using System.Globalization;
using System.Text.Json;
using AppointmentDesk.Host;
using WeavePort.Samples;

using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(60));
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};
try
{
    string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException("Run scripts/appointment-desk.sh; missing " + name);
    var runtime = new RuntimePaths(Required("WP_APPOINTMENT_ROOT"), Required("WP_APPOINTMENT_DOTNET"));
    if (args.Length == 2 && args[0] == "--activate")
    {
        runtime.Activate(args[1]);
        Console.WriteLine("Activated release " + args[1]);
        return 0;
    }

    if (args.Length == 1 && args[0] == "--verify")
    {
        await using var verificationCoordinator = new EmbeddedCoordinator();
        await Verification.RunAsync(runtime, verificationCoordinator, stop.Token);
        return 0;
    }

    string storePath = Path.Combine(runtime.Root, "calendar");
    string id = "demo-request", strategy = "earliest", tenant = "demo", profile = "default";
    var wish = Model.DefaultWish;
    bool loseResponse = false;
    string? holdAfterBooking = null;
    if (args.Length == 1 && args[0] == "--list-strategies")
    {
        Console.WriteLine("earliest/latest release " + runtime.Resolve().Identity.Version);
        return 0;
    }

    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--lose-response")
        {
            loseResponse = true;
            continue;
        }

        if (i + 1 >= args.Length)
        {
            throw new ArgumentException("Each option requires a value.");
        }

        string option = args[i], value = args[++i];
        switch (option)
        {
            case "--hold-after-booking":
                holdAfterBooking = value;
                break;
            case "--version":
                runtime = runtime with
                {
                    SelectedVersion = value
                };
                break;
            case "--store":
                storePath = value;
                break;
            case "--request":
                id = value;
                break;
            case "--strategy":
                strategy = value;
                break;
            case "--tenant":
                tenant = value;
                break;
            case "--profile":
                profile = value;
                break;
            case "--subject":
                wish = wish with
                {
                    Subject = value
                };
                break;
            case "--from":
                wish = wish with
                {
                    From = DateTimeOffset.Parse(value, CultureInfo.InvariantCulture)
                };
                break;
            case "--until":
                wish = wish with
                {
                    Until = DateTimeOffset.Parse(value, CultureInfo.InvariantCulture)
                };
                break;
            default:
                throw new ArgumentException("Unknown option: " + option);
        }
    }

    var outcome = await GuardedBooking.RunAsync(runtime, storePath, new Request(new Scope(tenant, profile), id, strategy, wish), stop.Token, loseResponse, holdAfterBooking);
    Console.WriteLine(JsonSerializer.Serialize(outcome, Model.Json));
    Console.WriteLine("Request: " + id + " | Calendar: " + Path.GetFullPath(Path.Combine(storePath, "calendar.json")));
    if (outcome.Status == "uncertain")
    {
        Console.WriteLine("Response interrupted. Repeat the identical request to reconcile; do not create a new request ID.");
    }

    return outcome.Status == "uncertain" ? 2 : 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Operation cancelled. Repeat the same request to inspect any retained intent.");
    return 130;
}
catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or InvalidOperationException or JsonException or FormatException)
{
    Console.Error.WriteLine("Appointment request refused: " + error.Message);
    return 1;
}
