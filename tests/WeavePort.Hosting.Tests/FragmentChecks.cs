using System.Text;
using WeavePort.Hosting;

internal static class FragmentChecks
{
    internal static async Task RunAsync()
    {
        // Forces prefix compaction, repeated growth and a second buffered frame.
        byte[] bytes = Encoding.UTF8.GetBytes("42\n\"" + new string('x', Frames.MaximumBytes - 2) + "\"\n17\n");
        using var input = new Fragments(bytes);
        using var frames = new Frames(input);
        if ((await frames.ReadAsync(default)).GetInt32() != 42) throw new Exception("Prefix frame.");
        var large = await frames.ReadAsync(default);
        if (large.GetString()!.Length != Frames.MaximumBytes - 2) throw new Exception("Fragmented maximum frame.");
        if ((await frames.ReadAsync(default)).GetInt32() != 17) throw new Exception("Following frame.");
        frames.Dispose();
        if (large.GetString()![^1] != 'x') throw new Exception("Owned frame after disposal.");
        Console.WriteLine("PASS 4 fragmented maximum frame/compaction/ownership assertions");
    }

    private sealed class Fragments(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => base.ReadAsync(buffer[..Math.Min(buffer.Length, 127)], token);
    }
}
