namespace DecisionRoom.Contracts;

public sealed record Proposal(string Id, string Title, int Cost, int Benefit);
public sealed record Priorities(int Benefit, int Cost, int Risk);
public sealed record Participant(string Id, string Language, string Profile, Priorities Priorities);
public sealed record RoomDefinition(Proposal[] Proposals, string[] Participants);
public sealed record Knowledge(string Source, Dictionary<string, int> Risks);
public sealed record Evaluation(string Participant, string Source, Dictionary<string, int> Risks, Dictionary<string, int> Scores, string PluginVersion = "1");
public sealed record RoomState(RoomDefinition Definition, Evaluation[] Evaluations);
public sealed record Reduction(RoomState State, Evaluation Evaluation);
public sealed record DecisionRequest(string Participant, RoomState Snapshot);
public sealed record RoomSnapshot(RoomState State, string? Winner);
public sealed record PluginIdentity(string Version);
