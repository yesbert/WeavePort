using System.Text.RegularExpressions;

namespace WeavePort.Hosting;

internal sealed record PythonSpecifier(string Operator, string Text, PythonVersion? Version, bool Prefix)
{
    private const int MaximumRequirementCharacters = 4096;
    internal static PythonSpecifier[] Parse(string requirement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requirement);
        if (requirement.Length > MaximumRequirementCharacters)
        {
            throw new FormatException("Python requirement exceeds limit.");
        }

        string[] parts = requirement.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            throw new FormatException("Python requirement contains no specifiers.");
        }

        return parts.Select(ParseOne).ToArray();
    }

    private static PythonSpecifier ParseOne(string text)
    {
        Match match = Regex.Match(text, @"^\s*(===|~=|==|!=|<=|>=|<|>)\s*(\S+)\s*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success)
        {
            throw new FormatException("Invalid Python version specifier.");
        }

        string comparisonOperator = match.Groups[1].Value;
        string value = match.Groups[2].Value;
        if (comparisonOperator == "===")
        {
            return new(comparisonOperator, value, null, false);
        }

        bool prefix = value.EndsWith(".*", StringComparison.Ordinal);
        PythonVersion version = PythonVersion.Parse(prefix ? value[..^2] : value);
        if (prefix && (comparisonOperator is not ("==" or "!=") || version.IsPrerelease || version.Post is not null || version.Local.Length != 0) || comparisonOperator == "~=" && version.Release.Length < 2 || comparisonOperator is not ("==" or "!=") && version.Local.Length != 0)
        {
            throw new FormatException("Invalid Python specifier operator/version combination.");
        }

        return new(comparisonOperator, value, version, prefix);
    }

    internal static bool Matches(string requirement, string observed)
    {
        PythonSpecifier[] constraints = Parse(requirement);
        PythonVersion candidate = PythonVersion.Parse(observed);
        bool prereleases = constraints.Any(specifier =>
            specifier.Operator is "==" or ">=" or "<=" or ">" or "<" or "~=" or "===" &&
            specifier.EnablesPrereleases());
        return (!candidate.IsPrerelease || prereleases) && constraints.All(specifier => specifier.Contains(candidate));
    }

    private bool EnablesPrereleases()
    {
        if (Version is not null)
        {
            return Version.IsPrerelease;
        }

        try
        {
            return PythonVersion.Parse(Text).IsPrerelease;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private bool Contains(PythonVersion candidate)
    {
        if (Operator == "===")
        {
            return string.Equals(Text, candidate.Canonical, StringComparison.OrdinalIgnoreCase);
        }

        PythonVersion bound = Version!;
        PythonVersion publicCandidate = candidate with
        {
            Local = []
        };
        int comparison = (bound.Local.Length == 0 ? publicCandidate : candidate).CompareTo(bound);
        bool equal = Prefix ? HasPrefix(candidate, bound, bound.Release.Length) : comparison == 0;
        return Operator switch
        {
            "==" => equal,
            "!=" => !equal,
            ">=" => comparison >= 0,
            "<=" => comparison <= 0,
            "<" => comparison < 0 && (bound.IsPrerelease || !candidate.IsPrerelease || candidate.CompareRelease(bound) != 0),
            ">" => comparison > 0 && (bound.Post is not null || candidate.Post is null || candidate.CompareRelease(bound) != 0) && (candidate.Local.Length == 0 || candidate.CompareRelease(bound) != 0),
            "~=" => comparison >= 0 && HasPrefix(candidate, bound, bound.Release.Length - 1),
            _ => false
        };
    }

    private static bool HasPrefix(PythonVersion candidate, PythonVersion bound, int length)
    {
        return candidate.Epoch == bound.Epoch && Enumerable.Range(0, length).All(i => (i < candidate.Release.Length ? candidate.Release[i] : 0) == bound.Release[i]);
    }
}
