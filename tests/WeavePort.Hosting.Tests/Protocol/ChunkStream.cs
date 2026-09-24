internal sealed class ChunkStream(byte[] bytes, int chunk) : MemoryStream(bytes)
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => base.ReadAsync(buffer[..Math.Min(buffer.Length, chunk)], token);
}
