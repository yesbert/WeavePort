using WeavePort.Hosting;
using System.Text.Json;
using AppointmentDesk.Contracts;

namespace AppointmentDesk.Host;
internal sealed record Scope(string Tenant, string Profile);
internal sealed record Request(Scope Scope, string Id, string Strategy, Wish Wish);
internal sealed record Entry(Request Request, Command Command, Outcome? Outcome, InstallationIdentity Installation);
internal sealed record Snapshot(int Schema, Entry[] Entries);
internal static class Model
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true
    };
    internal static readonly Slot[] Slots = Enumerable.Range(0, 6).Select(i => new Slot(new DateTimeOffset(2030, 1, 14, 9, 0, 0, TimeSpan.Zero).AddMinutes(i * 30), new DateTimeOffset(2030, 1, 14, 9, 30, 0, TimeSpan.Zero).AddMinutes(i * 30))).ToArray();
    internal static readonly Wish DefaultWish = new(Slots[0].Start, Slots[^1].End, "Project discussion");
    internal static void Validate(Request request)
    {
        if (request.Scope is null || !Text(request.Scope.Tenant) || !Text(request.Scope.Profile) || !Text(request.Id) || request.Strategy is not ("earliest" or "latest") || request.Wish is null || !Text(request.Wish.Subject) || request.Wish.From >= request.Wish.Until || request.Wish.From.Offset != TimeSpan.Zero || request.Wish.Until.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException("Invalid request; use installed strategy and bounded UTC wish.");
        }
    }

    internal static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128;
    internal static bool Fits(Wish wish, Slot slot) => Slots.Contains(slot) && slot.Start >= wish.From && slot.End <= wish.Until;
    internal static bool SameKey(Request a, Request b) => a.Scope == b.Scope && a.Id == b.Id;
}
