using System.Security.Cryptography;
using System.Text.Json;
using DecisionRoom.Contracts;

namespace DecisionRoom.Host;
internal sealed record RunConfiguration(string Tenant, Proposal[] Proposals, Participant[] Participants, Dictionary<string, Dictionary<string, int>> Knowledge, string? PluginVersion = null)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Tenant) || Proposals.Length != 3 || Participants.Length != 2 || Proposals.Select(p => p.Id).Distinct().Count() != 3 || Participants.Select(p => p.Id).Distinct().Count() != 2)
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

        foreach (var p in Participants)
        {
            if (string.IsNullOrWhiteSpace(p.Id) || p.Language is not ("csharp" or "python") || !Knowledge.TryGetValue(p.Profile, out var risks) || Proposals.Any(proposal => !risks.TryGetValue(proposal.Id, out int risk) || risk is < 0 or > 100) || new[]
            {
                p.Priorities.Benefit,
                p.Priorities.Cost,
                p.Priorities.Risk
            }.Any(w => w is < 0 or > 10))
            {
                throw new InvalidDataException("Invalid participant, priorities or knowledge profile.");
            }
        }
    }
}

internal static class Wire
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    internal static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
