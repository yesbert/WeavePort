using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;
using static PublisherFixture;

internal static class PublisherLifecycleChecks
{
    internal static async Task RunChildAsync()
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        start.ArgumentList.Add(typeof(PublisherLifecycleChecks).Assembly.Location);
        start.ArgumentList.Add("--publisher-lifecycle");
        using Process child = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await child.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
            }
        }

        Check(child.ExitCode == 0, "Publisher lifecycle child failed.");
    }

    internal static async Task RunAsync()
    {
        using RSA first = RSA.Create(3072);
        using RSA second = RSA.Create(3072);
        using var fixture = new PublisherFixture();
        fixture.Create();
        foreach (string file in Directory.EnumerateFiles(AppContext.BaseDirectory))
        {
            File.Copy(file, Path.Combine(fixture.Release(), Path.GetFileName(file)), true);
        }

        PluginInstallationSealer.Seal(fixture.Options() with
        {
            EntryPoints = new Dictionary<string, string> { ["dotnet"] = "WeavePort.Publisher.Tests.dll" },
            Launch = new()
            {
                Runtime = "dotnet",
                Arguments = ["--publisher-worker"]
            }
        });
        fixture.Sign(first);
        InstalledPlugin existing = await fixture.Catalog(Trust(first)).ResolveAsync("test", "1", "test/v1");
        await using var host = new PluginHost();
        await using IBoundPluginClient client = await host.BindAsync(existing, new()
        {
            TrustedCode = true
        },
            new("tenant-a", JsonElement.Parse("{}")), new NoCallbacks(), []);
        Check((await client.CallAsync("echo", JsonElement.Parse("42"))).GetInt32() == 42, "Signed worker failed.");
        Refuse(() => fixture.Catalog(Trust(second)).Resolve("test", "1", "test/v1", existing.Identity), "publisher-key-untrusted");
        Check((await client.CallAsync("echo", JsonElement.Parse("43"))).GetInt32() == 43, "Policy replacement unexpectedly revoked an existing worker.");
        Console.WriteLine("PASS existing signed worker remains host-owned after trust snapshot replacement");
    }

    private sealed class NoCallbacks : IHostCallbacks
    {
        public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
