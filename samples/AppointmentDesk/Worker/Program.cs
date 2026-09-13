using System.Text.Json;
using AppointmentDesk.Contracts;
using WeavePort.Sdk;

#if RELEASE_V2
const string release = "2";
#else
const string release = "1";
#endif
string strategy = args.Single();
if (strategy is not ("earliest" or "latest"))
{
    throw new ArgumentException("Unknown strategy.");
}

await new PluginApplication
{
    PluginVersion = release
}.Function<Wish, Proposal>("appointment.propose", async (wish, context, token) =>
{
    var response = await context.CallHostAsync("calendar.available", JsonSerializer.SerializeToElement(wish), token);
    var slots = response.Deserialize<Slot[]>() ?? throw new InvalidDataException("Missing availability.");
    return new Proposal(strategy == "earliest" ? slots.FirstOrDefault() : slots.LastOrDefault());
}).Function<Command, Outcome>("appointment.execute", async (command, context, token) =>
{
    var response = await context.CallHostAsync("calendar.book", JsonSerializer.SerializeToElement(command), token);
    return response.Deserialize<Outcome>() ?? throw new InvalidDataException("Missing booking result.");
}).RunAsync();
