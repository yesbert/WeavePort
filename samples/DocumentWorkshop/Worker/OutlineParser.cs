using System.Text;
using System.Text.RegularExpressions;
using DocumentWorkshop.Contracts;

namespace DocumentWorkshop.Worker;
internal sealed class OutlineParser(string kind, string fileName)
{
    private readonly StringBuilder _line = new();
    private readonly StringBuilder _body = new();
    private readonly HashSet<string> _anchors = [];
    private string _heading = fileName;
    private string _anchor = "section-1";
    private int _section = kind == "plain" ? 1 : 0;
    private int _part;
    private int _sequence;
    private int _level;
    private char _fence;
    private int _fenceLength;
    private bool _firstCharacter = true;
    private bool _previousCr;
    internal Queue<Fragment> Pending { get; } = new();
    internal bool Declined { get; private set; }

    internal void Feed(ReadOnlySpan<char> characters, bool end)
    {
        foreach (char character in characters)
        {
            bool skipBom = _firstCharacter && character == '\uFEFF';
            _firstCharacter = false;
            if (skipBom)
            {
                continue;
            }

            if (Declined)
            {
                break;
            }

            if (character == '\n' && _previousCr)
            {
                _previousCr = false;
                continue;
            }

            _previousCr = character == '\r';
            FeedCharacter(character);
        }

        if (!end || Declined)
        {
            return;
        }

        if (_line.Length > 0)
        {
            Line(_line.ToString());
            _line.Clear();
        }

        Flush(force: _section > 0 && _part == 0 && kind == "markdown");
    }

    private void FeedCharacter(char character)
    {
        if (character is '\r' or '\n')
        {
            Line(_line.ToString());
            _line.Clear();
            return;
        }

        if (_line.Length >= Limits.TextCharacters)
        {
            throw new InvalidDataException("Line exceeds 4096 characters.");
        }

        _line.Append(character);
    }

    private void Line(string line)
    {
        string trimmed = line.TrimStart(' ');
        bool heading = kind == "markdown" && _fence == '\0' && line.Length - trimmed.Length <= 3;
        int level = heading ? trimmed.TakeWhile(c => c == '#').Count() : 0;
        heading = level is >= 1 and <= 6 && (trimmed.Length == level || trimmed[level] == ' ');
        if (kind == "markdown" && _section == 0 && !heading)
        {
            Declined = !string.IsNullOrWhiteSpace(line);
            return;
        }

        if (heading)
        {
            BeginHeading(trimmed, level);
            return;
        }

        if (kind == "markdown" && trimmed.Length >= 3 && trimmed[0] is '`' or '~')
        {
            UpdateFence(trimmed);
        }

        Append(line + "\n");
    }

    private void Append(string text)
    {
        int offset = 0;
        while (offset < text.Length)
        {
            int take = FragmentLength(text, offset);
            if (take == 0)
            {
                Flush(false);
                continue;
            }

            _body.Append(text.AsSpan(offset, take));
            offset += take;
            if (_body.Length != Limits.TextCharacters)
            {
                continue;
            }

            Flush(false);
        }
    }

    private void Flush(bool force)
    {
        if (_body.Length == 0 && !force)
        {
            return;
        }

        if (_sequence >= Limits.TotalFragments)
        {
            throw new InvalidDataException("Too many fragments.");
        }

        Pending.Enqueue(new Fragment(_sequence++, _section, _part++, _heading, _level, _anchor, _body.ToString()));
        _body.Clear();
    }

    private void BeginHeading(string trimmed, int level)
    {
        Flush(force: _section > 0 && _part == 0);
        _section++;
        _part = 0;
        _level = level;
        _heading = trimmed[level..].Trim();
        var explicitAnchor = Regex.Match(_heading, @" \{#([A-Za-z0-9_-]{1,128})\}$");
        _anchor = explicitAnchor.Success ? explicitAnchor.Groups[1].Value : "section-" + _section;
        if (explicitAnchor.Success)
        {
            _heading = _heading[..explicitAnchor.Index];
        }

        if (_heading.Length > 256 || !_anchors.Add(_anchor))
        {
            throw new InvalidDataException("Invalid heading or duplicate anchor.");
        }

        return;
    }

    private void UpdateFence(string trimmed)
    {
        int length = trimmed.TakeWhile(c => c == trimmed[0]).Count();
        if (_fence == '\0' && length >= 3)
        {
            _fence = trimmed[0];
            _fenceLength = length;
        }
        else if (_fence == trimmed[0] && length >= _fenceLength && string.IsNullOrWhiteSpace(trimmed[length..]))
        {
            _fence = '\0';
        }
    }

    private int FragmentLength(string text, int offset)
    {
        int take = Math.Min(Limits.TextCharacters - _body.Length, text.Length - offset);
        if (offset + take < text.Length && take > 0 && char.IsHighSurrogate(text[offset + take - 1]) && char.IsLowSurrogate(text[offset + take]))
        {
            take--;
        }

        return take;
    }
}
