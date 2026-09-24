using System.Globalization;

namespace WeavePort.CapacityTests;

internal sealed class VmSampleParser(TimeProvider clock)
{
    private long _total, _available, _previousTicks, _previousIdle;
    private double _cpu;
    private string _pressure = "";

    internal VmSample? Read(string line)
    {
        string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
        {
            _total = long.Parse(words[1], CultureInfo.InvariantCulture) * 1024;
            return null;
        }
        if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
        {
            _available = long.Parse(words[1], CultureInfo.InvariantCulture) * 1024;
            return null;
        }
        if (line.StartsWith("some ", StringComparison.Ordinal))
        {
            _pressure = line;
            return null;
        }
        if (line.StartsWith("cpu ", StringComparison.Ordinal))
        {
            ReadCpu(words);
            return null;
        }
        return line == "END" ? new VmSample(clock.GetUtcNow(), _total, _available, _cpu, _pressure) : null;
    }

    private void ReadCpu(string[] words)
    {
        long[] values = words.Skip(1).Take(8).Select(x => long.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        long ticks = values.Sum();
        long idle = values[3] + values[4];
        _cpu = _previousTicks == 0 || ticks == _previousTicks ? 0 : 100d * (1 - (double)(idle - _previousIdle) / (ticks - _previousTicks));
        _previousTicks = ticks;
        _previousIdle = idle;
    }
}
