using DecisionRoom.Contracts;

namespace DecisionRoom.Host;

internal sealed record RunConfiguration(string Tenant, Proposal[] Proposals, Participant[] Participants, Dictionary<string, Dictionary<string, int>> Knowledge, string? PluginVersion = null)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Tenant) ||
            Proposals.Length != 3 || Participants.Length != 2 ||
            Proposals.Select(proposal => proposal.Id).Distinct().Count() != 3 ||
            Participants.Select(participant => participant.Id).Distinct().Count() != 2)
        {
            throw new InvalidDataException("Expected a tenant, three distinct proposals and two distinct participants.");
        }

        foreach (var p in Proposals)
        {
            if (string.IsNullOrWhiteSpace(p.Id) || p.Cost is < 0 or > 100 || p.Benefit is < 0 or > 100)
            {
                throw new InvalidDataException("Invalid proposal.");
            }
        }

        foreach (var participant in Participants)
        {
            ValidateParticipant(participant);
        }
    }

    private void ValidateParticipant(Participant participant)
    {
        if (string.IsNullOrWhiteSpace(participant.Id) ||
            participant.Language is not ("csharp" or "python") ||
            !Knowledge.TryGetValue(participant.Profile, out var risks))
        {
            throw new InvalidDataException("Invalid participant or knowledge profile.");
        }

        if (Proposals.Any(proposal => !risks.TryGetValue(proposal.Id, out int risk) || risk is < 0 or > 100))
        {
            throw new InvalidDataException("Knowledge must provide a risk from 0 to 100 for every proposal.");
        }

        Priorities priorities = participant.Priorities;
        if (priorities.Benefit is < 0 or > 10 || priorities.Cost is < 0 or > 10 || priorities.Risk is < 0 or > 10)
        {
            throw new InvalidDataException("Participant priorities must be between 0 and 10.");
        }
    }
}
