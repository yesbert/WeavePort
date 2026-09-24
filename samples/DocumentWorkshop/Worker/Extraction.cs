using System.Text;
using System.Text.Json;
using DocumentWorkshop.Contracts;
using WeavePort.Sdk;

namespace DocumentWorkshop.Worker;
internal sealed class Extraction(DocumentSource source, string kind)
{
    private readonly Decoder _decoder = new UTF8Encoding(false, true).GetDecoder();
    private readonly OutlineParser _parser = new(kind, source.FileName);
    private long _offset;
    private int _cursor;
    private bool _ended;
    private bool _complete;
    internal async Task<ExtractionPage> NextAsync(PageRequest request, PluginCallContext context, CancellationToken token)
    {
        if (_complete || request.Cursor != _cursor)
        {
            throw new ArgumentException("Invalid extraction cursor.");
        }

        if (_parser.Pending.Count == 0 && !_ended && !_parser.Declined)
        {
            await ReadSourceAsync(context, token);
        }

        var fragments = new List<Fragment>();
        while (fragments.Count < Limits.PageFragments && _parser.Pending.TryDequeue(out var fragment))
        {
            fragments.Add(fragment);
        }

        _complete = _parser.Declined || (_ended && _parser.Pending.Count == 0);
        return new ExtractionPage(++_cursor, _offset, _complete, _parser.Declined, fragments.ToArray(), _parser.Declined ? "The outline reader requires an ATX heading as its first nonblank line." : null);
    }

    private async Task ReadSourceAsync(PluginCallContext context, CancellationToken token)
    {
        var input = JsonSerializer.SerializeToElement(new ReadRequest(source.Id, _offset, Limits.ReadBytes), JsonSerializerOptions.Web);
        ReadReply reply = (await context.CallHostAsync("document.read", input, token)).Deserialize<ReadReply>(JsonSerializerOptions.Web)!;
        if (reply.Offset != _offset || reply.Data.Length > Limits.ReadBytes || _offset + reply.Data.Length > source.Length || reply.End != (_offset + reply.Data.Length == source.Length) || (!reply.End && reply.Data.Length == 0))
        {
            throw new InvalidDataException("Invalid source transfer.");
        }

        var characters = new char[Limits.ReadBytes + 2];
        int count = _decoder.GetChars(reply.Data, characters, reply.End);
        _parser.Feed(characters.AsSpan(0, count), reply.End);
        _offset += reply.Data.Length;
        _ended = reply.End;
    }
}
