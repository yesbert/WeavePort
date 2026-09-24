using DocumentWorkshop.Contracts;

namespace DocumentWorkshop.Host;

internal sealed class PageValidator(long length)
{
    private int _cursor;
    private long _bytes;
    private Fragment? _last;
    private readonly HashSet<string> _anchors = [];
    private bool _finished;
    internal int Fragments { get; private set; }
    internal int Sections { get; private set; }

    internal void Accept(ExtractionPage page)
    {
        if (page is null || _finished || page.NextCursor != _cursor + 1 ||
            page.BytesRead < _bytes || page.BytesRead > length ||
            page.Fragments is null || page.Fragments.Length > Limits.PageFragments)
        {
            throw new InvalidDataException("Invalid extraction page or progress.");
        }

        if (page.Declined && (!page.Complete || Fragments > 0 || page.Fragments.Length > 0))
        {
            throw new InvalidDataException("A declined extraction must complete without fragments.");
        }

        if (page.Complete && !page.Declined && page.BytesRead != length)
        {
            throw new InvalidDataException("A completed extraction must consume the whole document.");
        }

        if (!page.Complete && page.BytesRead == _bytes && page.Fragments.Length == 0)
        {
            throw new InvalidDataException("An unfinished extraction must make progress.");
        }

        foreach (var fragment in page.Fragments)
        {
            AcceptFragment(fragment);
        }

        _cursor = page.NextCursor;
        _bytes = page.BytesRead;
        _finished = page.Complete;
    }

    private void AcceptFragment(Fragment f)
    {
        if (f is null || f.Sequence != Fragments ||
            f.Text is null || f.Text.Length > Limits.TextCharacters ||
            f.Heading is null || f.Heading.Length > Limits.HeadingCharacters ||
            string.IsNullOrEmpty(f.Anchor) || f.Anchor.Length > Limits.AnchorCharacters ||
            f.Level is < 0 or > 6 || Fragments >= Limits.TotalFragments)
        {
            throw new InvalidDataException("Malformed fragment.");
        }

        if (f.Section == Sections + 1)
        {
            BeginSection(f);
        }
        else if (_last is null || f.Section != Sections || f.Part != _last.Part + 1 || f.Heading != _last.Heading || f.Level != _last.Level || f.Anchor != _last.Anchor)
        {
            throw new InvalidDataException("Invalid fragment order or section identity.");
        }

        _last = f;
        Fragments++;
    }

    private void BeginSection(Fragment f)
    {
        if (f.Part != 0 || !_anchors.Add(f.Anchor))
        {
            throw new InvalidDataException("Invalid section start.");
        }

        Sections++;
    }
}
