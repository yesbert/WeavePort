using WeavePort.Hosting;
using System.Text.Json;
using AppointmentDesk.Contracts;

namespace AppointmentDesk.Host;

internal sealed record Scope(string Tenant, string Profile);
internal sealed record Request(Scope Scope, string Id, string Strategy, Wish Wish);
internal sealed record Entry(Request Request, Command Command, Outcome? Outcome, InstallationIdentity Installation);
internal sealed record Snapshot(int Schema, Entry[] Entries);
