using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;

internal static class LanguagePolicy
{
    internal static async Task RunAsync(Evidence evidence)
    {
        await using var host = new PluginHost();
        foreach (string language in new[] { "csharp", "python", "typescript" })
        {
            await using IPluginSession session = await host.BindAsync(new PluginContext("policy", "test", "1", "default", JsonSerializer.SerializeToElement(new
            {
            })),
                new DockerProfile("weaveport-poc-" + language + ":1"), new DeniedCallbacks(), []);
            InvocationResult result = await session.InvokeAsync("echo", JsonSerializer.SerializeToElement(new
            {
                text = "synthetic"
            }));
            if (result.Status != "ok")
            {
                throw new InvalidDataException("Language fixture failed startup.");
            }

            Policy.Validate(await Policy.InspectAsync(session.Instance));
            int before = evidence.Failures;
            await SecurityObservations.RunAsync(evidence, "language-" + language, session.Instance);
            if (evidence.Failures != before)
            {
                throw new InvalidDataException("Language policy preflight failed.");
            }
        }
    }
    private sealed class DeniedCallbacks : IHostCallbacks
    {
        public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken) => throw new UnauthorizedAccessException();
    }
}
