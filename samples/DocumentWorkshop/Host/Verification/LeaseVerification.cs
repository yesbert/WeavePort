using System.Text.Json;
using DocumentWorkshop.Contracts;
using WeavePort.Abstractions;

namespace DocumentWorkshop.Host;

internal static class LeaseVerification
{
    internal static async Task RunAsync(string root, CancellationToken token)
    {
        string path = Path.Combine(root, "lease.txt");
        await File.WriteAllTextAsync(path, new string('a', Limits.ReadBytes), token);
        var source = new DocumentSource("opaque-id", "lease.txt", "text/plain", Limits.ReadBytes);
        await using var lease = new SourceLease(path, "tenant-a", "profile-a", source);
        HostCall Call(ReadRequest request, string tenant = "tenant-a", string profile = "profile-a") => new(new PluginContext(tenant, "plain", "1", profile, JsonSerializer.SerializeToElement(new
        {
        })), "invocation", "document.read", JsonSerializer.SerializeToElement(request, Json.Options), "trace");
        foreach (var call in new[]
        {
            Call(new("opaque-id", 0, 1), tenant: "tenant-b"),
            Call(new("opaque-id", 0, 1), profile: "profile-b"),
            Call(new("foreign-id", 0, 1)),
            Call(new("opaque-id", -1, 1)),
            Call(new("opaque-id", source.Length + 1, 1)),
            Call(new("opaque-id", 0, Limits.ReadBytes + 1)),
            Call(new("opaque-id", 0, 0))
        })
        {
            await RefusedAsync(() => lease.InvokeAsync(call, token).AsTask());
        }

        Verification.Check(lease.Calls == 0, "foreign tenant/profile/id and invalid ranges return no bytes");
        var reply = (await lease.InvokeAsync(Call(new("opaque-id", 0, Limits.ReadBytes)), token)).Deserialize<ReadReply>(Json.Options)!;
        Verification.Check(reply.Data.Length == Limits.ReadBytes && reply.End, "valid bounded lease read succeeds");
        await lease.InvokeAsync(Call(new("opaque-id", 0, Limits.ReadBytes)), token);
        await RefusedAsync(() => lease.InvokeAsync(Call(new("opaque-id", 0, 1)), token).AsTask());
        Verification.Check(lease.Calls == 2, "cumulative transfer budget is enforced");
        await lease.DisposeAsync();
        await RefusedAsync(() => lease.InvokeAsync(Call(new("opaque-id", 0, 1)), token).AsTask());
        Verification.Check(lease.Revoked && lease.Calls == 2, "disposed document identity cannot be reused");
    }

    private static async Task RefusedAsync(Func<Task<JsonElement>> action)
    {
        try
        {
            await action();
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        throw new InvalidDataException("Expected lease refusal.");
    }
}
