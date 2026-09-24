using DecisionRoom.Contracts;

internal static class RoomOperations
{
    internal static RoomState Reduce(Reduction request)
    {
        if (!request.State.Definition.Participants.Contains(request.Evaluation.Participant) ||
            request.State.Evaluations.Any(evaluation => evaluation.Participant == request.Evaluation.Participant))
        {
            throw new ArgumentException("Unknown or duplicate participant.");
        }

        return request.State with
        {
            Evaluations = [.. request.State.Evaluations, request.Evaluation]
        };
    }

    internal static RoomSnapshot Snapshot(RoomState state)
    {
        if (state.Evaluations.Length != state.Definition.Participants.Length)
        {
            return new RoomSnapshot(state, null);
        }

        string winner = state.Definition.Proposals
            .OrderByDescending(proposal => state.Evaluations.Sum(evaluation => evaluation.Scores[proposal.Id]))
            .ThenBy(proposal => proposal.Id, StringComparer.Ordinal)
            .First().Id;
        return new RoomSnapshot(state, winner);
    }
}
