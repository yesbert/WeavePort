using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace WeavePort.Hosting;

internal sealed partial record PythonVersion(BigInteger Epoch, BigInteger[] Release, PythonReleasePhase PrereleasePhase, BigInteger PrereleaseNumber, BigInteger? Post, BigInteger? Dev, string[] Local)
{
    internal string Canonical => FormatCanonical();
    internal bool IsPrerelease => PrereleasePhase < PythonReleasePhase.Final || Dev is not null;

    private string FormatCanonical()
    {
        var version = new StringBuilder();
        if (Epoch != 0)
        {
            version.Append(Epoch).Append('!');
        }

        version.Append(string.Join('.', Release));
        string prerelease = PrereleasePhase switch
        {
            PythonReleasePhase.Alpha => "a",
            PythonReleasePhase.Beta => "b",
            PythonReleasePhase.ReleaseCandidate => "rc",
            _ => ""
        };
        if (prerelease.Length > 0)
        {
            version.Append(prerelease).Append(PrereleaseNumber);
        }

        if (Post is { } post)
        {
            version.Append(".post").Append(post);
        }

        if (Dev is { } development)
        {
            version.Append(".dev").Append(development);
        }

        if (Local.Length > 0)
        {
            version.Append('+').Append(string.Join('.', Local.Select(CanonicalLocalPart)));
        }

        return version.ToString();
    }

    private static string CanonicalLocalPart(string part) =>
        BigInteger.TryParse(part, out var number) ? number.ToString() : part;

    [GeneratedRegex(@"^\s*v?(?:(?<epoch>[0-9]+)!)?(?<release>[0-9]+(?:\.[0-9]+)*)(?:[-_.]?(?<pre>a|b|c|rc|alpha|beta|pre|preview)[-_.]?(?<pren>[0-9]+)?)?(?:(?:-(?<post>[0-9]+))|(?:[-_.]?(?:post|rev|r)[-_.]?(?<post>[0-9]+)?))?(?:[-_.]?dev[-_.]?(?<dev>[0-9]+)?)?(?:\+(?<local>[a-z0-9]+(?:[-_.][a-z0-9]+)*))?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Pattern();
    internal static PythonVersion Parse(string text)
    {
        Match match = Pattern().Match(text);
        if (!match.Success)
        {
            throw new FormatException("Invalid Python version.");
        }

        string pre = match.Groups["pre"].Value.ToLowerInvariant();
        PythonReleasePhase phase = pre switch
        {
            "a" or "alpha" => PythonReleasePhase.Alpha,
            "b" or "beta" => PythonReleasePhase.Beta,
            "c" or "rc" or "pre" or "preview" => PythonReleasePhase.ReleaseCandidate,
            _ => PythonReleasePhase.Final
        };
        string publicText = text.Split('+')[0];
        bool post = match.Groups["post"].Success || Regex.IsMatch(publicText, @"(?:post|rev|r)[-_.]?\s*$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        bool dev = publicText.Contains("dev", StringComparison.OrdinalIgnoreCase);
        return new(Number(match, "epoch"), match.Groups["release"].Value.Split('.').Select(BigInteger.Parse).ToArray(), phase, Number(match, "pren"), post ? Number(match, "post") : null, dev ? Number(match, "dev") : null, match.Groups["local"].Success ? match.Groups["local"].Value.ToLowerInvariant().Split('-', '_', '.') : []);
    }

    private static BigInteger Number(Match match, string group) => match.Groups[group].Value is { Length: > 0 } text ? BigInteger.Parse(text) : BigInteger.Zero;
    // A bare development release sorts before alpha; a development suffix on a
    // prerelease keeps that prerelease phase and is compared later.
    private PythonReleasePhase Phase => PrereleasePhase == PythonReleasePhase.Final && Post is null && Dev is not null
        ? PythonReleasePhase.Development
        : PrereleasePhase;

    internal int CompareTo(PythonVersion other)
    {
        int result = Epoch.CompareTo(other.Epoch);
        if (result != 0)
        {
            return result;
        }

        result = CompareRelease(other);
        if (result != 0)
        {
            return result;
        }

        result = Phase.CompareTo(other.Phase);
        if (result != 0)
        {
            return result;
        }

        result = PrereleaseNumber.CompareTo(other.PrereleaseNumber);
        if (result != 0)
        {
            return result;
        }

        result = Nullable.Compare(Post, other.Post);
        if (result != 0)
        {
            return result;
        }

        result = CompareDevelopment(Dev, other.Dev);
        return result != 0 ? result : CompareLocal(other);
    }

    private static int CompareDevelopment(BigInteger? left, BigInteger? right)
    {
        if (left is null)
        {
            return right is null ? 0 : 1;
        }

        return right is null ? -1 : left.Value.CompareTo(right.Value);
    }

    internal int CompareRelease(PythonVersion other)
    {
        for (int i = 0; i < Math.Max(Release.Length, other.Release.Length); i++)
        {
            int result = (i < Release.Length ? Release[i] : 0).CompareTo(i < other.Release.Length ? other.Release[i] : 0);
            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }

    private int CompareLocal(PythonVersion other)
    {
        for (int i = 0; i < Math.Min(Local.Length, other.Local.Length); i++)
        {
            int result = CompareLocalPart(Local[i], other.Local[i]);
            if (result != 0)
            {
                return result;
            }
        }

        return Local.Length.CompareTo(other.Local.Length);
    }

    private static int CompareLocalPart(string leftPart, string rightPart)
    {
        bool leftNumber = BigInteger.TryParse(leftPart, out var left);
        bool rightNumber = BigInteger.TryParse(rightPart, out var right);
        if (leftNumber && rightNumber)
        {
            return left.CompareTo(right);
        }

        return leftNumber != rightNumber ? leftNumber.CompareTo(rightNumber) : string.CompareOrdinal(leftPart, rightPart);
    }
}
