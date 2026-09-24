internal sealed class OffsetClock : TimeProvider
{
    private long _offset;
    public override long GetTimestamp() => TimeProvider.System.GetTimestamp() + Interlocked.Read(ref _offset);
    internal void Advance(TimeSpan time) => Interlocked.Add(ref _offset, (long)(time.TotalSeconds * TimestampFrequency));
}
