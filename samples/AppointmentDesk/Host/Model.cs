using WeavePort.Hosting;
using System.Text.Json;
using AppointmentDesk.Contracts;

namespace AppointmentDesk.Host;

internal static class Model
{
    private const int MaximumTextCharacters = 128;
    private const int SlotCount = 6;
    private const int SlotMinutes = 30;
    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true
    };
    // Fixed demonstration availability, deliberately independent of the wall clock.
    private static readonly DateTimeOffset Opening = new(2030, 1, 14, 9, 0, 0, TimeSpan.Zero);
    internal static readonly Slot[] Slots = Enumerable.Range(0, SlotCount)
        .Select(index => new Slot(Opening.AddMinutes(index * SlotMinutes), Opening.AddMinutes((index + 1) * SlotMinutes)))
        .ToArray();
    internal static readonly Wish DefaultWish = new(Slots[0].Start, Slots[^1].End, "Project discussion");
    internal static void Validate(Request request)
    {
        if (request.Scope is null || !IsValidText(request.Scope.Tenant) || !IsValidText(request.Scope.Profile) ||
            !IsValidText(request.Id) || request.Strategy is not ("earliest" or "latest") ||
            request.Wish is null || !IsValidText(request.Wish.Subject) ||
            request.Wish.From >= request.Wish.Until ||
            request.Wish.From.Offset != TimeSpan.Zero || request.Wish.Until.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException("Invalid request; use installed strategy and bounded UTC wish.");
        }
    }

    internal static bool IsValidText(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumTextCharacters;
    internal static bool Fits(Wish wish, Slot slot) => Slots.Contains(slot) && slot.Start >= wish.From && slot.End <= wish.Until;
    internal static bool SameKey(Request a, Request b) => a.Scope == b.Scope && a.Id == b.Id;
}
