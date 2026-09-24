using System.Text.Json;
using AppointmentDesk.Contracts;
using WeavePort.Abstractions;

namespace AppointmentDesk.Host;

internal sealed class BookingCallbacks(CalendarStore store, Request request, string version = "1") : IHostCallbacks
{
    internal Command? Approved { get; set; }
    internal Func<Task>? AfterBooking { get; set; }
    internal int BookCalls { get; private set; }

    public async ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken token)
    {
        if (call.Context.Tenant != request.Scope.Tenant || call.Context.Profile != request.Scope.Profile || call.Context.Plugin != request.Strategy || call.Context.Version != version)
        {
            throw new UnauthorizedAccessException("Foreign calendar scope.");
        }

        if (call.Operation == "calendar.available")
        {
            return await AvailableAsync(call, token);
        }

        if (call.Operation != "calendar.book" || Approved is null || call.Payload.Deserialize<Command>() != Approved)
        {
            throw new UnauthorizedAccessException("Unapproved calendar action.");
        }

        var outcome = await store.BookAsync(request, Approved, token);
        BookCalls++;
        if (AfterBooking is not null)
        {
            await AfterBooking();
        }

        return JsonSerializer.SerializeToElement(outcome);
    }

    private async ValueTask<JsonElement> AvailableAsync(HostCall call, CancellationToken token)
    {
        if (call.Payload.Deserialize<Wish>() != request.Wish)
        {
            throw new UnauthorizedAccessException("Changed wish.");
        }

        return JsonSerializer.SerializeToElement(await store.AvailableAsync(request, token));
    }
}
