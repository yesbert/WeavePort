using System.Text.Json;
using DecisionRoom.Contracts;
using WeavePort.Sdk;

internal sealed class Strategy(string release, int riskMultiplier)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal async ValueTask<Evaluation> EvaluateAsync(DecisionRequest request, PluginCallContext context, CancellationToken token)
    {
        Priorities weights = context.Configuration.GetProperty("priorities").Deserialize<Priorities>(Json)!;
        JsonElement reply = await context.CallHostAsync("knowledge.read", JsonSerializer.SerializeToElement(new
        {
        }), token);
        Knowledge knowledge = reply.Deserialize<Knowledge>(Json)!;
        var scores = request.Snapshot.Definition.Proposals.ToDictionary(
            proposal => proposal.Id,
            proposal => proposal.Benefit * weights.Benefit - proposal.Cost * weights.Cost -
                knowledge.Risks[proposal.Id] * weights.Risk * riskMultiplier);
        return new Evaluation(request.Participant, knowledge.Source, knowledge.Risks, scores, release);
    }
}
