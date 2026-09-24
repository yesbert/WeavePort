internal sealed class TestClock : TimeProvider
{
    private long _now;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _now;
    public void Advance(TimeSpan duration) => _now += duration.Ticks;
}
