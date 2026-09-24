using WeavePort.Internal;
using System.Text.Json;
using WeavePort.Abstractions;

namespace WeavePort.Testing;
/// <summary>Reusable checks for sample contracts and experiment evidence.</summary>
public static class ContractChecks
{
    /// <summary>Checks a completed invocation without embedding a specific test framework.</summary>
    public static JsonElement Successful(InvocationResult result)
    {
        if (result.Status != FailureCodes.Ok)
        {
            throw new InvalidOperationException($"Expected ok, received {result.Status}.");
        }

        return result.Value;
    }

    /// <summary>Computes a nearest-rank percentile over nonempty samples.</summary>
    public static double Percentile(IEnumerable<double> samples, double percentile)
    {
        if (percentile is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percentile));
        }

        double[] ordered = samples.Order().ToArray();
        if (ordered.Length == 0)
        {
            throw new ArgumentException("Samples cannot be empty.", nameof(samples));
        }

        return ordered[Math.Clamp((int)Math.Ceiling(percentile / 100 * ordered.Length) - 1, 0, ordered.Length - 1)];
    }
}
