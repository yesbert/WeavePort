internal sealed class CancelStream : MemoryStream
{
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        await Task.Delay(Timeout.Infinite, token);
        return 0;
    }
}
