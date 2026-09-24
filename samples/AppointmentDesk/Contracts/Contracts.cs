namespace AppointmentDesk.Contracts;

public sealed record Wish(DateTimeOffset From, DateTimeOffset Until, string Subject);
public sealed record Slot(DateTimeOffset Start, DateTimeOffset End);
public sealed record Proposal(Slot? Slot);
public sealed record Command(string RequestId, Slot Slot, string Subject);
public sealed record Outcome(string Status, string? BookingId, Slot? Slot);
