using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk;
using WeavePort.Sdk.Client;

internal static class VersionChecks
{
    internal static async Task RunAsync()
    {
        VerifyAssemblyVersions();
        string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException(name);
        string root = Required("WP_VERSION_ROOT");
        string dotnet = Required("WP_VERSION_DOTNET");
        string node = Required("WP_VERSION_NODE");
        string python = Path.Combine(root, "python/bin/python");
        string self = typeof(Callbacks).Assembly.Location;
        await using var host = new PluginHost(options: new WorkerPoolOptions(MaximumPristineWorkers: 0));
        int checks = 0;
        foreach (var fixture in new[]
        {
            (Language: "csharp", Executable: dotnet, Entry: self),
            (Language: "python", Executable: python, Entry: Path.Combine(root, "worker.py")),
            (Language: "typescript", Executable: node, Entry: Path.Combine(root, "worker.mjs"))
        })
        {
            checks += await VerifyLanguageAsync(host, root, fixture.Language, fixture.Executable, fixture.Entry);
        }
        Console.WriteLine($"{checks} packed SDK version checks passed.");
    }
    private static void VerifyAssemblyVersions()
    {
        var entryVersion = typeof(Callbacks).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion;
        if (PluginApplication.VersionFromAssembly() != entryVersion.Split('+')[0] || PluginApplication.VersionFromAssembly(includeBuildMetadata: true) != entryVersion)
        {
            throw new Exception("Assembly version selection failed.");
        }

        var versionedAssembly = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new System.Reflection.AssemblyName("VersionFixture"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
        versionedAssembly.SetCustomAttribute(new System.Reflection.Emit.CustomAttributeBuilder(typeof(System.Reflection.AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!, ["1.2.3-beta+commit"]));
        if (PluginApplication.VersionFromAssembly(versionedAssembly) != "1.2.3-beta" || PluginApplication.VersionFromAssembly(versionedAssembly, true) != "1.2.3-beta+commit")
        {
            throw new Exception("Prerelease or metadata changed.");
        }

        var missingVersion = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new System.Reflection.AssemblyName("MissingVersionFixture"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
        try
        {
            PluginApplication.VersionFromAssembly(missingVersion);
            throw new Exception("Missing informational metadata guessed.");
        }
        catch (InvalidOperationException) { }

    }
    private static async Task<int> VerifyLanguageAsync(PluginHost host, string root, string language, string executable, string original)
    {
        int checks = 0;
        foreach (var pair in new (string Declared, string Expected)[] { ("default", "1"), ("2", "2"), ("2", "1"), ("", "1") })
        {
            await VerifyCaseAsync(host, root, language, executable, original, pair.Declared, pair.Expected);
            checks++;
        }
        return checks;
    }
    private static async Task VerifyCaseAsync(PluginHost host, string root, string language, string executable,
        string original, string declared, string expected)
    {
        string entry = CompatibilityFixture.Resolve(root, language, executable, original, expected);
        string[] arguments = language == "csharp" ? [entry, "worker", declared]
            : [entry, declared];
        var profile = new ProcessProfile(executable, arguments, trustedCode: true,
            workspaceRoot: Path.Combine(root, "workers"), timeout: TimeSpan.FromSeconds(5));
        var session = await host.BindAsync(new PluginContext("sdk-version-tests", "echo", expected, "default",
            JsonSerializer.SerializeToElement(new
            {
            })), profile, new Callbacks(), []);
        await using var client = new LocalPluginClient(session);
        bool mustSucceed = declared == "default" || declared == expected;
        try
        {
            var result = await client.CallAsync("echo", JsonSerializer.SerializeToElement(new
            {
                value = "version-check"
            }));
            if (!mustSucceed || result.GetProperty("value").GetString() != "version-check")
            {
                throw new InvalidDataException("Unexpected version acceptance/result.");
            }
        }
        catch (PluginCallException error) when (!mustSucceed && error.Status is "version-mismatch" or "failed")
        {
            if (error.MayHaveExecuted)
            {
                throw new Exception("Version failure dispatched work.");
            }

            if (declared.Length > 0 && (error.VersionMismatch?.Expected != expected || error.VersionMismatch.Advertised != declared))
            {
                throw new Exception("Version diagnostic lost.");
            }
        }
        Console.WriteLine($"PASS: {language}, declared='{declared}', expected='{expected}'");
    }
}
