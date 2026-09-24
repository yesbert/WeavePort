namespace DocumentWorkshop.Contracts;

public static class Limits
{
    public const int SourceBytes = 8 * 1024 * 1024;
    public const int ReadBytes = 32 * 1024;
    public const int HeadingCharacters = 256;
    public const int AnchorCharacters = 128;
    public const int TextCharacters = 4096;
    public const int PageFragments = 32;
    public const int TotalFragments = 8192;
    public const int OutputBytes = 32 * 1024 * 1024;
}

public sealed record ReaderInfo(string Id, string Version, string[] MediaTypes);
public sealed record DocumentSource(string Id, string FileName, string MediaType, long Length);
public sealed record ReadRequest(string DocumentId, long Offset, int Count);
public sealed record ReadReply(long Offset, byte[] Data, bool End);
public sealed record Fragment(int Sequence, int Section, int Part, string Heading, int Level, string Anchor, string Text);
public sealed record PageRequest(int Cursor);
public sealed record ExtractionPage(int NextCursor, long BytesRead, bool Complete, bool Declined, Fragment[] Fragments, string? Reason = null);
