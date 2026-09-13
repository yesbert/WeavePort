using System.Text.Json;
using DecisionRoom.Contracts;
using WeavePort.Sdk;

#if DECISION_V2
const string release = "2";
const int riskMultiplier = 10;
#else
const string release = "1";
const int riskMultiplier = 1;
#endif
await new PluginApplication
{
    PluginVersion = release
}.Function<object, PluginIdentity>("artifact.identity", (_, _, _) => ValueTask.FromResult(new PluginIdentity(release))).Function<RoomDefinition, RoomState>("room.initialize", (definition, _, _) => ValueTask.FromResult(new RoomState(definition, []))).Function<Reduction, RoomState>("room.reduce", (request, _, _) =>
{
    if (!request.State.Definition.Participants.Contains(request.Evaluation.Participant) || request.State.Evaluations.Any(e => e.Participant == request.Evaluation.Participant))
    {
        throw new ArgumentException("Unknown or duplicate participant.");
    }

    return ValueTask.FromResult(request.State with { Evaluations = [..request.State.Evaluations, request.Evaluation] });
}).Function<RoomState, RoomSnapshot>("room.snapshot", (state, _, _) =>
{
    string? winner = state.Evaluations.Length == state.Definition.Participants.Length ? state.Definition.Proposals.OrderByDescending(p => state.Evaluations.Sum(e => e.Scores[p.Id])).ThenBy(p => p.Id, StringComparer.Ordinal).First().Id : null;
    return ValueTask.FromResult(new RoomSnapshot(state, winner));
}).Function<DecisionRequest, Evaluation>("strategy.evaluate", async (request, context, token) =>
{
    var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    Priorities weights = context.Configuration.GetProperty("priorities").Deserialize<Priorities>(json)!;
    Knowledge knowledge = (await context.CallHostAsync("knowledge.read", JsonSerializer.SerializeToElement(new { }), token)).Deserialize<Knowledge>(json)!;
    var scores = request.Snapshot.Definition.Proposals.ToDictionary(p => p.Id, p => p.Benefit * weights.Benefit - p.Cost * weights.Cost - knowledge.Risks[p.Id] * weights.Risk * riskMultiplier);
    return new Evaluation(request.Participant, knowledge.Source, knowledge.Risks, scores, release);
}).RunAsync();
