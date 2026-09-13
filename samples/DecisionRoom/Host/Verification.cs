using System.Diagnostics;
using System.Text.Json;
using DecisionRoom.Contracts;
using WeavePort.Sdk.Client;

namespace DecisionRoom.Host;
internal static class Verification
{
    internal static async Task RunAsync(RuntimePaths runtime, CancellationToken token)
    {
        string directory = Path.Combine(runtime.Root, "verification", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var config = JsonSerializer.Deserialize<RunConfiguration>(await File.ReadAllTextAsync("samples/DecisionRoom/config.json", token), Wire.Json)!;
        config = config with
        {
            PluginVersion = "1"
        };
        var runner = new RoomRunner(runtime, TextWriter.Null);
        var scenarios = new Scenarios(runtime, config, runner, directory, token);
        await scenarios.ParityAsync();
        await scenarios.RecoveryAndGrantsAsync();
        await scenarios.IsolationAsync();
        await scenarios.JournalGuardsAsync();
        await VersionVerification.RunAsync(runtime, config, directory, token);
        await File.WriteAllTextAsync(Path.Combine(directory, "result.txt"), "PASS: deterministic flow, language parity, priority substitution, worker restart/loss, resume, callback denial, concurrent profiles journal guards and parallel plugin releases.\n" + System.Runtime.InteropServices.RuntimeInformation.OSDescription + "\n" + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + "\n", token);
        Console.WriteLine("Verification passed. Evidence: " + directory);
    }

    private sealed class Scenarios(RuntimePaths runtime, RunConfiguration config, RoomRunner runner, string directory, CancellationToken token)
    {
        private RoomSnapshot _baseline = null!;
        private RunConfiguration _foreign = null!;
        private string JournalPath(string name) => Path.Combine(directory, name + ".json");
        private Task<RoomSnapshot> RunAsync(string name, RunConfiguration configuration, bool resume = false) => runner.RunAsync(configuration, new RunOptions(JournalPath(name), Resume: resume), token);
        internal async Task ParityAsync()
        {
            _baseline = await RunAsync("baseline", config);
            Check(_baseline.Winner == "B" && _baseline.State.Evaluations.Length == 2, "complete default run selects B");
            var replay = await RunAsync("baseline", config, true);
            Check(Wire.Serialize(replay) == Wire.Serialize(_baseline), "completed journal replays identically");
            foreach (string language in new[]
            {
                "csharp",
                "python"
            }

            )
            {
                var alternate = config with
                {
                    Participants = config.Participants.Select(p => p with { Language = language }).ToArray()
                };
                var result = await RunAsync(language, alternate);
                Check(Wire.Serialize(result) == Wire.Serialize(_baseline), language + " shared contract parity");
            }

            var economy = config with
            {
                Participants = config.Participants.Select(p => p with { Priorities = new(1, 4, 1) }).ToArray()
            };
            var impact = config with
            {
                Participants = config.Participants.Select(p => p with { Priorities = new(4, 1, 1) }).ToArray()
            };
            Check((await RunAsync("economy", economy)).Winner == "A", "economy priorities select A");
            Check((await RunAsync("impact", impact)).Winner == "C", "impact priorities select C");
        }

        internal async Task RecoveryAndGrantsAsync()
        {
            var restarted = await runner.RunAsync(config, new RunOptions(JournalPath("restart"), RestartAfterFirst: true), token);
            Check(Wire.Serialize(restarted) == Wire.Serialize(_baseline), "explicit worker replacement preserves result");
            var paused = await runner.RunAsync(config, new RunOptions(JournalPath("pause"), PauseAfterFirst: true), token);
            Check(paused.Winner is null && paused.State.Evaluations.Length == 1, "pause retains exactly one committed event");
            Check(Wire.Serialize(await RunAsync("pause", config, true)) == Wire.Serialize(_baseline), "fresh host resumes paused journal");
            await ThrowsAsync<SimulatedLoss>(async () => await runner.RunAsync(config, new RunOptions(JournalPath("killed")), token, async session =>
            {
                string instance = session.Instance;
                int index = instance.LastIndexOf("-p", StringComparison.Ordinal);
                if (index < 0)
                {
                    throw new InvalidDataException("Cannot identify owned native worker.");
                }

                using var worker = Process.GetProcessById(int.Parse(instance[(index + 2)..]));
                worker.Kill();
                await worker.WaitForExitAsync(token);
                throw new SimulatedLoss();
            }));
            Check(Wire.Serialize(await RunAsync("killed", config, true)) == Wire.Serialize(_baseline), "abrupt worker loss after commit resumes identically");
            foreach (string language in new[]
            {
                "csharp",
                "python"
            }

            )
            {
                var deniedConfig = config with
                {
                    Participants = config.Participants.Select(p => p with { Language = language }).ToArray()
                };
                var callbacks = new KnowledgeCallbacks(deniedConfig);
                string path = JournalPath("denied-" + language);
                await ThrowsAsync<PluginCallException>(async () => await runner.RunAsync(deniedConfig, new RunOptions(path, DenyKnowledge: true), token, callbacks: callbacks));
                var journal = JsonSerializer.Deserialize<JournalData>(await File.ReadAllTextAsync(path, token), Wire.Json)!;
                Check(callbacks.Calls == 0 && journal.Events.Length == 0, language + " denied callback releases no knowledge and commits no evaluation");
            }
        }

        internal async Task IsolationAsync()
        {
            _foreign = config with
            {
                Tenant = "other-customer",
                Knowledge = new()
                {
                    ["economy"] = new()
                    {
                        ["A"] = 80,
                        ["B"] = 0,
                        ["C"] = 0
                    },
                    ["impact"] = new()
                    {
                        ["A"] = 0,
                        ["B"] = 80,
                        ["C"] = 0
                    }
                }
            };
            var concurrent = await Task.WhenAll(RunAsync("parallel-a", config), RunAsync("parallel-b", _foreign));
            Check(Wire.Serialize(concurrent[0]) == Wire.Serialize(_baseline), "parallel run A retains own result");
            foreach (var e in concurrent[1].State.Evaluations)
            {
                var p = _foreign.Participants.Single(p => p.Id == e.Participant);
                Check(e.Source == _foreign.Tenant + "/" + p.Profile && e.Risks.All(pair => pair.Value == _foreign.Knowledge[p.Profile][pair.Key]), "parallel run B profile " + p.Profile);
            }

            var sameTenant = config with
            {
                Participants = config.Participants.Select(p => p with { Profile = p.Profile + "-alternate" }).ToArray(),
                Knowledge = _foreign.Knowledge.ToDictionary(pair => pair.Key + "-alternate", pair => pair.Value)
            };
            var sharedCustomer = await Task.WhenAll(RunAsync("same-a", config), RunAsync("same-b", sameTenant));
            Check(Wire.Serialize(sharedCustomer[0]) == Wire.Serialize(_baseline), "same-customer run A retains own profiles");
            Check(sharedCustomer[1].State.Evaluations.All(e => e.Source.EndsWith("-alternate", StringComparison.Ordinal)), "same-customer run B uses its alternate profiles");
        }

        internal async Task JournalGuardsAsync()
        {
            string before = await File.ReadAllTextAsync(JournalPath("baseline"), token);
            await ThrowsAsync<InvalidDataException>(async () => await RunAsync("baseline", _foreign, true));
            Check(await File.ReadAllTextAsync(JournalPath("baseline"), token) == before, "changed configuration refuses resume without overwrite");
            using (var held = new Journal(JournalPath("baseline"), config, runtime, true))
            {
                await ThrowsAsync<IOException>(async () => await RunAsync("baseline", config, true));
            }

            Check(true, "exclusive journal writer enforced");
            string altered = JournalPath("artifact-mismatch");
            var saved = JsonSerializer.Deserialize<JournalData>(before, Wire.Json)!;
            saved.Artifacts[saved.Artifacts.Keys.First()] = "changed";
            await File.WriteAllTextAsync(altered, Wire.Serialize(saved), token);
            await ThrowsAsync<InvalidDataException>(async () => await RunAsync("artifact-mismatch", config, true));
            Check(true, "changed artifact identity refuses resume");
        }
    }

    private static void Check(bool condition, string label)
    {
        if (!condition)
        {
            throw new InvalidDataException("FAIL: " + label);
        }

        Console.WriteLine("PASS: " + label);
    }

    private static async Task ThrowsAsync<T>(Func<Task> action)
        where T : Exception
    {
        try
        {
            await action();
        }
        catch (T)
        {
            return;
        }

        throw new InvalidDataException("Expected " + typeof(T).Name);
    }

    private sealed class SimulatedLoss : Exception;
}
