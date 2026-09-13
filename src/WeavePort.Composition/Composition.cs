using System.Text.Json;
using WeavePort.Abstractions;

namespace WeavePort.Composition;
/// <summary>Technical composition primitives. Products own contracts, session identity, ordering and merge semantics.</summary>
public static class Composition
{
    /// <summary>Executes one externally bound plugin over bounded base64 chunks. The plugin must implement the bulk-map contract.</summary>
    public static Task<ResultHandle> MapAsync(ResultScope scope, ResultHandle input, IPluginSession session, int chunkBytes = 65536, CancellationToken cancellationToken = default)
    {
        if (!StringComparer.Ordinal.Equals(scope.Tenant, session.Tenant))
        {
            throw new UnauthorizedAccessException("Plugin binding belongs to another tenant.");
        }

        return scope.TransformAsync(input, async (bytes, token) =>
        {
            InvocationResult result = await session.InvokeAsync("bulk-map", JsonSerializer.SerializeToElement(new BulkChunk(bytes), BulkJson.Default.BulkChunk), token);
            if (result.Status != "ok")
            {
                throw new IOException("Plugin stage failed: " + result.Status);
            }

            token.ThrowIfCancellationRequested();
            byte[] decoded = result.Value.GetProperty("data").GetBytesFromBase64();
            if (decoded.Length > chunkBytes)
            {
                throw new InvalidDataException("Output chunk too large.");
            }

            return decoded;
        }, chunkBytes, cancellationToken);
    }

    /// <summary>Runs required branches with bounded parallelism. Any failure cancels siblings and awaits started operations.</summary>
    public static async Task<ResultHandle[]> FanOutAsync(ResultScope scope, ResultHandle input, IReadOnlyList<Func<ResultScope, ResultHandle, CancellationToken, Task<ResultHandle>>> branches, int maximumParallelism, CancellationToken cancellationToken = default)
    {
        if (maximumParallelism < 1 || branches.Count is < 1 or > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumParallelism));
        }

        var results = new ResultHandle[branches.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, branches.Count), new ParallelOptions { MaxDegreeOfParallelism = maximumParallelism, CancellationToken = cancellationToken }, async (index, token) => results[index] = await branches[index](scope, input, token));
        return results;
    }

    /// <summary>Concatenates immutable results in declared order. Suitable for NDJSON streams; this is not a generic semantic merge or reranker.</summary>
    public static Task<ResultHandle> ConcatenateAsync(ResultScope scope, IReadOnlyList<ResultHandle> inputs, CancellationToken cancellationToken = default) => scope.CreateAsync(async (destination, token) =>
    {
        foreach (ResultHandle input in inputs)
        {
            await scope.CopyToAsync(input, destination, token);
        }
    }, cancellationToken);
}
