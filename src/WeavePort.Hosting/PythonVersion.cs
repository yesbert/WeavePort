using System.Numerics;
using System.Text.RegularExpressions;

namespace WeavePort.Hosting;
internal sealed partial record PythonVersion(BigInteger Epoch, BigInteger[] Release, int PreKind, BigInteger Pre, BigInteger? Post, BigInteger? Dev, string[] Local) : IComparable<PythonVersion>
{
    internal string Canonical => (Epoch == 0 ? "" : Epoch + "!") + string.Join('.', Release) + (PreKind < 3 ? new[]
    {
        "a",
        "b",
        "rc"
    }[PreKind] + Pre : "") + (Post is { } post ? ".post" + post : "") + (Dev is { } dev ? ".dev" + dev : "") + (Local.Length == 0 ? "" : "+" + string.Join('.', Local.Select(part => BigInteger.TryParse(part, out var number) ? number.ToString() : part)));
    internal bool IsPrerelease => PreKind < 3 || Dev is not null;

    [GeneratedRegex(@"^\s*v?(?:(?<epoch>[0-9]+)!)?(?<release>[0-9]+(?:\.[0-9]+)*)(?:[-_.]?(?<pre>a|b|c|rc|alpha|beta|pre|preview)[-_.]?(?<pren>[0-9]+)?)?(?:(?:-(?<post>[0-9]+))|(?:[-_.]?(?:post|rev|r)[-_.]?(?<post>[0-9]+)?))?(?:[-_.]?dev[-_.]?(?<dev>[0-9]+)?)?(?:\+(?<local>[a-z0-9]+(?:[-_.][a-z0-9]+)*))?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Pattern();
    internal static PythonVersion Parse(string text)
    {
        Match m = Pattern().Match(text);
        if (!m.Success)
        {
            throw new FormatException("Invalid Python version.");
        }

        string pre = m.Groups["pre"].Value.ToLowerInvariant();
        int kind = pre switch
        {
            "a" or "alpha" => 0,
            "b" or "beta" => 1,
            "c" or "rc" or "pre" or "preview" => 2,
            _ => 3
        };
        string publicText = text.Split('+')[0];
        bool post = m.Groups["post"].Success || Regex.IsMatch(publicText, @"(?:post|rev|r)[-_.]?\s*$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        bool dev = publicText.Contains("dev", StringComparison.OrdinalIgnoreCase);
        return new(Number(m, "epoch"), m.Groups["release"].Value.Split('.').Select(BigInteger.Parse).ToArray(), kind, Number(m, "pren"), post ? Number(m, "post") : null, dev ? Number(m, "dev") : null, m.Groups["local"].Success ? Regex.Split(m.Groups["local"].Value.ToLowerInvariant(), "[-_.]") : []);
    }

    private static BigInteger Number(Match match, string group) => match.Groups[group].Value is { Length: > 0 } text ? BigInteger.Parse(text) : BigInteger.Zero;
    public int CompareTo(PythonVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

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

        int phase = PreKind == 3 && Post is null && Dev is not null ? -1 : PreKind;
        int otherPhase = other.PreKind == 3 && other.Post is null && other.Dev is not null ? -1 : other.PreKind;
        result = phase.CompareTo(otherPhase);
        if (result == 0)
        {
            result = Pre.CompareTo(other.Pre);
        }

        if (result == 0)
        {
            result = Nullable.Compare(Post, other.Post);
        }

        if (result == 0)
        {
            result = Dev is null ? (other.Dev is null ? 0 : 1) : other.Dev is null ? -1 : Dev.Value.CompareTo(other.Dev.Value);
        }

        return result != 0 ? result : CompareLocal(other);
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
            bool leftNumber = BigInteger.TryParse(Local[i], out var left);
            bool rightNumber = BigInteger.TryParse(other.Local[i], out var right);
            int result = leftNumber && rightNumber ? left.CompareTo(right) : leftNumber != rightNumber ? leftNumber.CompareTo(rightNumber) : string.CompareOrdinal(Local[i], other.Local[i]);
            if (result != 0)
            {
                return result;
            }
        }

        return Local.Length.CompareTo(other.Local.Length);
    }
}
