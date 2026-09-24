using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WeavePort.Abstractions;
using WeavePort.Composition;

internal sealed record DocumentRow(int Id, int Score, string Text);
internal sealed record WorkloadResult(string Tenant, int Documents, int Branches, long InputBytes, long IntermediateBytes,
    int[] TopIds, double ComputeMs, double AiMs);
[JsonSerializable(typeof(DocumentRow))]
[JsonSerializable(typeof(WorkloadResult))]
internal partial class WorkloadJson : JsonSerializerContext;

internal static class DocumentWorkload
{
    internal static async Task<WorkloadResult> RunAsync(string root, string tenant, IPluginSession[] sessions,
        WorkloadOptions options, Stream? export, CancellationToken token)
    {
        long began = Stopwatch.GetTimestamp();
        await using var scope = new ResultScope(root, tenant, new ResultLimits(256L << 20, 1L << 30, 16));
        ResultHandle input = await scope.CreateAsync(async (stream, ct) =>
        {
            byte[] newline = [10];
            string text = new('a', options.TextBytes);
            for (int id = 0; id < options.Documents; id++)
            {
                byte[] row = JsonSerializer.SerializeToUtf8Bytes(new DocumentRow(id, id, text), WorkloadJson.Default.DocumentRow);
                await stream.WriteAsync(row, ct);
                await stream.WriteAsync(newline, ct);
            }
        }, token);
        ResultHandle result = input;
        int branches = options.Kind is "rag" or "large" ? 3 : 1;
        result = await TransformAsync(scope, input, sessions, options.Kind, branches, token);
        using var reducer = new DocumentReducer(options.Documents, options.TextBytes, branches);
        await scope.CopyToAsync(result, reducer, token);
        reducer.Complete();
        if (export is not null)
        {
            await scope.CopyToAsync(result, export, token);
        }

        double compute = Stopwatch.GetElapsedTime(began).TotalMilliseconds;
        long ai = Stopwatch.GetTimestamp();
        if (options.AiMs > 0)
        {
            await Task.Delay(options.AiMs, token);
        }

        return new(tenant, options.Documents, branches, input.Length, result.Length,
                    reducer.TopIds(), compute, Stopwatch.GetElapsedTime(ai).TotalMilliseconds);
    }
    private static async Task<ResultHandle> TransformAsync(ResultScope scope, ResultHandle input,
        IPluginSession[] sessions, string kind, int branches, CancellationToken token)
    {
        if (branches == 3)
        {
            var steps = sessions.Select<IPluginSession, Func<ResultScope, ResultHandle, CancellationToken, Task<ResultHandle>>>(session =>
                (owner, handle, ct) => Composition.MapAsync(owner, handle, session, 65536, ct)).ToArray();
            ResultHandle[] results = await Composition.FanOutAsync(scope, input, steps, 3, token);
            return await Composition.ConcatenateAsync(scope, results, token);
        }

        ResultHandle result = input;
        foreach (IPluginSession session in sessions.Take(kind == "search" ? 1 : 3))
        {
            result = await Composition.MapAsync(scope, result, session, 65536, token);
        }

        return result;
    }

}

internal sealed record WorkloadOptions(string Kind, int Documents, int TextBytes, int AiMs);
