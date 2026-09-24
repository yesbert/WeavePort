using DecisionRoom.Contracts;

namespace DecisionRoom.Host;

internal static class VersionVerification
{
    internal static async Task RunAsync(RuntimePaths runtime, RunConfiguration original, string directory, CancellationToken token)
    {
        var config = original with
        {
            PluginVersion = null
        };
        runtime = runtime with
        {
            Selector = Path.Combine(directory, "active-version.txt")
        };
        runtime.Activate("1");
        var runner = new RoomRunner(runtime, TextWriter.Null);
        string PathFor(string name) => Path.Combine(directory, "version-" + name + ".json");
        async Task<RoomSnapshot> Run(string name, RunConfiguration configuration, bool resume = false) => await runner.RunAsync(configuration, new RunOptions(PathFor(name), Resume: resume), token);
        var v1 = await Run("one", config);
        var v2 = await Run("two", config with
        {
            PluginVersion = "2"
        });
        Check(v1.Winner == "B" && v2.Winner == "A", "distinct installed releases produce B and A");
        await VerifyLanguageParityAsync(runner, config, directory, v2, token);

        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<RoomSnapshot> live = runner.RunAsync(config, new RunOptions(PathFor("live"), RestartAfterFirst: true), token, async _ =>
        {
            reached.SetResult();
            await release.Task.WaitAsync(token);
        });
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(20), token);
            runtime.Activate("2");
            var newcomer = await Run("new-default", config);
            Check(!live.IsCompleted && Wire.Serialize(newcomer) == Wire.Serialize(v2), "new default runs v2 while v1 remains live");
        }
        finally
        {
            release.TrySetResult();
            var completed = await live;
            Check(Wire.Serialize(completed) == Wire.Serialize(v1), "live v1 keeps its release after activation and worker replacement");
        }

        await runner.RunAsync(config with
        {
            PluginVersion = "1"
        }, new RunOptions(PathFor("paused"), PauseAfterFirst: true), token);
        Check(Wire.Serialize(await Run("paused", config, true)) == Wire.Serialize(v1), "resume pins journal version despite new default");
        string before = await File.ReadAllTextAsync(PathFor("paused"), token);
        await RefusedAsync(async () => await Run("paused", config with { PluginVersion = "2" }, true));
        Check(await File.ReadAllTextAsync(PathFor("paused"), token) == before, "conflicting explicit version cannot overwrite journal");
        await RefusedAsync(async () => await Run("invalid", config with { PluginVersion = "../2" }));
        Check(!File.Exists(PathFor("invalid")), "invalid selection refuses before journal commit");
        await VerifyArtifactFailuresAsync(runtime, config, directory, token);
    }

    private static async Task VerifyLanguageParityAsync(RoomRunner runner, RunConfiguration config, string directory, RoomSnapshot expected, CancellationToken token)
    {
        string[] languages = ["csharp", "python"];
        foreach (string language in languages)
        {
            var configuration = config with
            {
                PluginVersion = "2",
                Participants = config.Participants.Select(participant => participant with { Language = language }).ToArray()
            };
            string journal = Path.Combine(directory, "version-two-" + language + ".json");
            RoomSnapshot result = await runner.RunAsync(configuration, new RunOptions(journal), token);
            Check(Wire.Serialize(result) == Wire.Serialize(expected), "version 2 " + language + " parity and artifact identity");
        }
    }

    private static async Task VerifyArtifactFailuresAsync(RuntimePaths runtime, RunConfiguration config, string directory, CancellationToken token)
    {
        string copies = Path.Combine(directory, "release-copies");
        foreach (string version in new[]
        {
            "1",
            "2"
        })
        {
            string destination = Path.Combine(copies, version);
            Directory.CreateDirectory(destination);
            CopyRelease(runtime.ReleaseRoot(version), destination);
        }

        var isolated = runtime with
        {
            ReleaseDirectory = copies
        };
        var runner = new RoomRunner(isolated, TextWriter.Null);
        var pinned = config with
        {
            PluginVersion = "1"
        };
        string path = Path.Combine(directory, "artifact-pinned.json");
        var initial = await runner.RunAsync(pinned, new RunOptions(path, PauseAfterFirst: true), token);
        await File.AppendAllTextAsync(isolated.PythonPlugin("2"), "\n# other release changed\n", token);
        var completed = await runner.RunAsync(config, new RunOptions(path, Resume: true), token);
        Check(initial.Winner is null && completed.Winner == "B", "unselected release changes do not invalidate v1 journal");
        string before = await File.ReadAllTextAsync(path, token);
        await File.AppendAllTextAsync(isolated.PythonPlugin("1"), "\n# selected release changed\n", token);
        await RefusedAsync(async () => await runner.RunAsync(config, new RunOptions(path, Resume: true), token));
        Check(await File.ReadAllTextAsync(path, token) == before, "selected artifact mutation refuses resume without overwrite");
        File.Delete(isolated.Plugin("1"));
        await RefusedAsync(async () => await runner.RunAsync(config, new RunOptions(path, Resume: true), token));
        Check(await File.ReadAllTextAsync(path, token) == before, "missing pinned release cannot fall forward to active v2");
        File.Copy(runtime.Plugin("1"), isolated.Plugin("2"), overwrite: true);
        await RefusedAsync(async () => await runner.RunAsync(config with { PluginVersion = "2" }, new RunOptions(Path.Combine(directory, "wrong-binary.json")), token));
        Check(true, "installation digest rejects a v1 binary copied over v2");
        File.WriteAllText(runtime.SelectorPath, "unsupported\n");
        await RefusedAsync(async () => await runner.RunAsync(config, new RunOptions(Path.Combine(directory, "bad-default.json")), token));
        Check(true, "unsupported active default is refused");
    }

    private static void Check(bool condition, string label)
    {
        if (!condition)
        {
            throw new InvalidDataException("FAIL: " + label);
        }

        Console.WriteLine("PASS: " + label);
    }

    private static async Task RefusedAsync(Func<Task> action, bool protocolMismatch = false)
    {
        try
        {
            await action();
        }
        catch (InvalidDataException) when (!protocolMismatch)
        {
            return;
        }
        catch (WeavePort.Sdk.Client.PluginCallException error) when (protocolMismatch && error.Status == "protocol-error")
        {
            return;
        }

        throw new InvalidDataException("Expected refusal.");
    }

    private static void CopyRelease(string source, string destination)
    {
        foreach (string file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
    }
}
