using System.Text.Json;
using DecisionRoom.Contracts;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;

namespace DecisionRoom.Host;
internal sealed record RunOptions(string Journal, bool Resume = false, bool RestartAfterFirst = false, bool DenyKnowledge = false, bool PauseAfterFirst = false);
internal sealed class RoomRunner(RuntimePaths runtime, TextWriter output)
{
    internal async Task<RoomSnapshot> RunAsync(RunConfiguration config, RunOptions options, CancellationToken token, Func<IPluginSession, Task>? afterFirstCommit = null, KnowledgeCallbacks? callbacks = null)
    {
        config.Validate();
        using var journal = new Journal(options.Journal, config, runtime, options.Resume);
        config = journal.Configuration;
        callbacks ??= new KnowledgeCallbacks(config);
        await using var host = new PluginHost(options: new WorkerPoolOptions(MaximumPristineWorkers: 0));
        string workspace = Path.Combine(runtime.Root, "workers", Guid.NewGuid().ToString("N"));
        var room = await BindAsync(host, new PluginContext(config.Tenant, "decision-room", config.PluginVersion!, "room", JsonSerializer.SerializeToElement(new { })), "csharp", workspace, callbacks, []);
        await using var roomClient = new LocalPluginClient(room);
        await VerifyReleaseAsync(roomClient, config.PluginVersion!, token);
        var definition = new RoomDefinition(config.Proposals, config.Participants.Select(p => p.Id).ToArray());
        RoomState state = await roomClient.CallAsync<RoomDefinition, RoomState>("room.initialize", definition, token);
        EnsureState(state, definition, []);
        if (!options.Resume)
        {
            await journal.CommitAsync([], token);
        }

        foreach (Evaluation evaluation in journal.Data.Events)
        {
            ValidateEvaluation(evaluation, config.Participants[state.Evaluations.Length], config);
            state = await ReduceAsync(roomClient, state, evaluation, token);
        }

        await output.WriteLineAsync($"Run: {config.Tenant}; plugin version: {config.PluginVersion}; committed evaluations: {state.Evaluations.Length}");
        foreach (var participant in config.Participants.Skip(state.Evaluations.Length))
        {
            journal.VerifyArtifacts(runtime);
            var strategy = await BindAsync(host, new PluginContext(config.Tenant, "decision-room", config.PluginVersion!, participant.Profile, JsonSerializer.SerializeToElement(new { priorities = participant.Priorities }, Wire.Json)), participant.Language, workspace, callbacks, options.DenyKnowledge ? [] : ["knowledge.read"]);
            await using var client = new LocalPluginClient(strategy);
            await VerifyReleaseAsync(client, config.PluginVersion!, token);
            Evaluation evaluation = await client.CallAsync<DecisionRequest, Evaluation>("strategy.evaluate", new DecisionRequest(participant.Id, state), token);
            ValidateEvaluation(evaluation, participant, config);
            RoomState next = await ReduceAsync(roomClient, state, evaluation, token);
            await journal.CommitAsync(next.Evaluations, token);
            state = next;
            await output.WriteLineAsync($"{participant.Id} ({participant.Language}, {participant.Profile}) used {evaluation.Source}");
            foreach (var p in config.Proposals)
            {
                await output.WriteLineAsync($"  {p.Id}: {p.Title}; cost={p.Cost}, benefit={p.Benefit}, risk={evaluation.Risks[p.Id]}, score={evaluation.Scores[p.Id]}");
            }

            if (state.Evaluations.Length == 1)
            {
                if (afterFirstCommit is not null)
                {
                    await afterFirstCommit(room);
                }

                if (options.RestartAfterFirst)
                {
                    await RestartRoomAsync(journal, room, roomClient, config.PluginVersion!, state, token);
                }

                if (options.PauseAfterFirst)
                {
                    break;
                }
            }
        }

        return await CompleteRoomAsync(roomClient, state, definition, config, token);
    }

    private async Task<RoomSnapshot> CompleteRoomAsync(IPluginClient roomClient, RoomState state, RoomDefinition definition, RunConfiguration config, CancellationToken token)
    {
        RoomSnapshot snapshot = await roomClient.CallAsync<RoomState, RoomSnapshot>("room.snapshot", state, token);
        EnsureState(snapshot.State, definition, state.Evaluations);
        bool complete = state.Evaluations.Length == 2;
        if (complete ? !config.Proposals.Any(p => p.Id == snapshot.Winner) : snapshot.Winner is not null)
        {
            throw new InvalidDataException("Invalid completion result.");
        }

        await output.WriteLineAsync(complete ? $"Winner: {snapshot.Winner}" : "Paused after first committed evaluation; resume with the same configuration.");
        return snapshot;
    }

    private async Task RestartRoomAsync(Journal journal, IPluginSession room, IPluginClient roomClient, string version, RoomState state, CancellationToken token)
    {
        journal.VerifyArtifacts(runtime);
        string previous = room.Instance;
        await room.RestartAsync(token);
        await VerifyReleaseAsync(roomClient, version, token);
        await roomClient.CallAsync<RoomState, RoomSnapshot>("room.snapshot", state, token);
        if (room.Instance == previous)
        {
            throw new InvalidDataException("Worker was not replaced.");
        }

        await output.WriteLineAsync("Room worker replaced; committed state remains application-owned.");
    }

    private async Task<IPluginSession> BindAsync(PluginHost host, PluginContext context, string language, string workspace, IHostCallbacks callbacks, string[] grants)
    {
        var installed = runtime.Catalog().Resolve("decision-room", context.Version, "decision-room/v1");
        var process = language == "python" ? new ProcessProfile(runtime.Python, ["-B", installed.EntryPoints["python"]], true, workspace, timeout: TimeSpan.FromSeconds(10)) : new ProcessProfile(runtime.Dotnet, [installed.EntryPoints["dotnet"]], true, workspace, timeout: TimeSpan.FromSeconds(10));
        return await host.BindAsync(context, process, callbacks, grants);
    }

    private static async Task VerifyReleaseAsync(IPluginClient client, string version, CancellationToken token)
    {
        var identity = await client.CallAsync<object, PluginIdentity>("artifact.identity", new { }, token);
        if (identity.Version != version)
        {
            throw new InvalidDataException("Plugin artifact reports a different release.");
        }
    }

    private static async Task<RoomState> ReduceAsync(IPluginClient room, RoomState state, Evaluation evaluation, CancellationToken token)
    {
        RoomState next = await room.CallAsync<Reduction, RoomState>("room.reduce", new Reduction(state, evaluation), token);
        EnsureState(next, state.Definition, [..state.Evaluations, evaluation]);
        return next;
    }

    private static void EnsureState(RoomState state, RoomDefinition definition, Evaluation[] evaluations)
    {
        if (Wire.Serialize(state) != Wire.Serialize(new RoomState(definition, evaluations)))
        {
            throw new InvalidDataException("Room returned an invalid state transition.");
        }
    }

    private static void ValidateEvaluation(Evaluation evaluation, Participant participant, RunConfiguration config)
    {
        var ids = config.Proposals.Select(p => p.Id).ToHashSet();
        if (evaluation.PluginVersion != config.PluginVersion || evaluation.Participant != participant.Id || evaluation.Source != config.Tenant + "/" + participant.Profile || !ids.SetEquals(evaluation.Scores.Keys) || !ids.SetEquals(evaluation.Risks.Keys) || evaluation.Scores.Values.Any(v => v is < -12000 or > 12000) || ids.Any(id => evaluation.Risks[id] != config.Knowledge[participant.Profile][id]))
        {
            throw new InvalidDataException("Invalid strategy evaluation or knowledge scope.");
        }
    }
}
