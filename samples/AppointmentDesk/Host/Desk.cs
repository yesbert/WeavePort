using System.Text.Json;
using AppointmentDesk.Contracts;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;
using WeavePort.Samples;

namespace AppointmentDesk.Host;
internal sealed record RuntimePaths(string Root, string Dotnet, string? SelectedVersion = null, string? Selector = null, string? WorkerRoot = null)
{
    internal string SelectorPath => Selector ?? Path.Combine(Root, "active-version.txt");
    internal InstalledPluginCatalog Catalog => new(Path.Combine(Root, "releases"), new Dictionary<string, string> { ["dotnet"] = Dotnet });

    internal InstalledPlugin Resolve(InstallationIdentity? pin = null)
    {
        if (pin is not null && SelectedVersion is not null && SelectedVersion != pin.Version)
        {
            throw new InvalidDataException("Explicit release conflicts with retained installation.");
        }

        return Catalog.Resolve("appointment-desk", pin?.Version ?? SelectedVersion ?? InstalledPluginCatalog.ReadSelection(SelectorPath), "appointment-desk/v1", pin);
    }

    internal void Activate(string version) => Catalog.Activate(SelectorPath, "appointment-desk", version, "appointment-desk/v1");
}

internal sealed record Hooks(Func<IPluginSession, BookingCallbacks, Task>? Bound = null, Func<Task>? Prepared = null, bool DenyBook = false);
internal sealed class Desk(RuntimePaths runtime, CalendarStore store, EmbeddedCoordinator coordinator)
{
    internal Task<Outcome> RunAsync(Request request, CancellationToken token, Hooks? hooks = null) => coordinator.RunAsync((host, admittedToken) => RunCoreAsync(host, request, admittedToken, hooks), token);
    private async Task<Outcome> RunCoreAsync(PluginHost host, Request request, CancellationToken token, Hooks? hooks)
    {
        var entry = await store.FindAsync(request, token);
        var installed = runtime.Resolve(entry?.Installation);
        var callbacks = new BookingCallbacks(store, request, installed.Identity.Version);
        var process = new ProcessProfile(runtime.Dotnet, [installed.EntryPoints["dotnet"], request.Strategy], trustedCode: true, workspaceRoot: runtime.WorkerRoot ?? Path.Combine(runtime.Root, "workers"), timeout: TimeSpan.FromSeconds(10));
        var session = await host.BindAsync(new PluginContext(request.Scope.Tenant, request.Strategy, installed.Identity.Version, request.Scope.Profile, JsonSerializer.SerializeToElement(new { })), process, callbacks, hooks?.DenyBook == true ? ["calendar.available"] : ["calendar.available", "calendar.book"], token);
        await using var client = new LocalPluginClient(session);
        if (hooks?.Bound is not null)
        {
            await hooks.Bound(session, callbacks);
        }

        if (entry is null)
        {
            var proposal = await client.CallAsync<Wish, Proposal>("appointment.propose", request.Wish, token);
            if (proposal is null)
            {
                throw new InvalidDataException("Missing proposal.");
            }

            if (proposal.Slot is null)
            {
                return new Outcome("unavailable", null, null);
            }

            entry = await store.PrepareAsync(request, proposal.Slot, token, installed.Identity);
            if (entry.Installation != installed.Identity)
            {
                throw new InvalidDataException("Concurrent request selected another installation; retry exact request.");
            }
        }

        callbacks.Approved = entry.Command;
        if (hooks?.Prepared is not null)
        {
            await hooks.Prepared();
        }

        _ = runtime.Resolve(entry.Installation);
        try
        {
            var outcome = await client.CallAsync<Command, Outcome>("appointment.execute", entry.Command, token);
            var recorded = await store.FindAsync(request, token);
            if (outcome is null || recorded?.Outcome != outcome)
            {
                throw new InvalidDataException("Worker result differs from committed calendar.");
            }

            return outcome;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or OperationCanceledException or InvalidOperationException)
        {
            // Dispatch may have committed. The same request must reconcile the recorded command.
            return new Outcome("uncertain", null, entry.Command.Slot);
        }
    }
}
