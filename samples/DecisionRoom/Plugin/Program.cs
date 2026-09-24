using DecisionRoom.Contracts;
using WeavePort.Sdk;

#if DECISION_V2
const string release = "2";
const int riskMultiplier = 10;
#else
const string release = "1";
const int riskMultiplier = 1;
#endif
var strategy = new Strategy(release, riskMultiplier);
await new PluginApplication { PluginVersion = release }
    .Function<object, PluginIdentity>("artifact.identity", (_, _, _) => ValueTask.FromResult(new PluginIdentity(release)))
    .Function<RoomDefinition, RoomState>("room.initialize", (definition, _, _) => ValueTask.FromResult(new RoomState(definition, [])))
    .Function<Reduction, RoomState>("room.reduce", (request, _, _) => ValueTask.FromResult(RoomOperations.Reduce(request)))
    .Function<RoomState, RoomSnapshot>("room.snapshot", (state, _, _) => ValueTask.FromResult(RoomOperations.Snapshot(state)))
    .Function<DecisionRequest, Evaluation>("strategy.evaluate", strategy.EvaluateAsync)
    .RunAsync();
