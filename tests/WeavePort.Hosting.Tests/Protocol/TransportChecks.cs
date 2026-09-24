using System.Text;
using System.Diagnostics;
using System.Text.Json;
using WeavePort.Hosting;

internal sealed class TransportChecks
{
    private int _checks;

    internal static async Task RunAsync()
    {
        await new TransportChecks().RunAllAsync();
    }

    private async Task RunAllAsync()
    {
        await SplitFramesAsync();
        await RejectedFramesAsync();
        await ExactCeilingAsync();
        await CancellationAsync();
        await AtomicOutputAsync();
        await DiagnosticsAsync();
        Console.WriteLine($"PASS {_checks} transport assertions: split/multiple/UTF8/ownership/limits/depth/EOF/cancellation/atomic output");
    }

    private async Task SplitFramesAsync()
    {
        foreach (int chunk in new[] { 1, 7, 4096, 1048576 })
        {
            string first = "{\"text\":\"Grüße 🐝\"}";
            string large = "\"" + new string('x', 65536) + "\"";
            using var stream = new ChunkStream(Encoding.UTF8.GetBytes(first + "\n" + large + "\n42\n"), chunk);
            using var frames = new Frames(stream);
            JsonElement owned = await frames.ReadAsync(default);
            Assert((await frames.ReadAsync(default)).GetString()!.Length == 65536, "large split frame");
            Assert((await frames.ReadAsync(default)).GetInt32() == 42, "carryover");
            frames.Dispose();
            Assert(owned.GetProperty("text").GetString() == "Grüße 🐝", "owned lifetime");
        }
    }

    private async Task RejectedFramesAsync()
    {
        foreach ((byte[] data, Type expected) in new[]
        {
    (Encoding.UTF8.GetBytes("{\n"), typeof(JsonException)),
    (Encoding.UTF8.GetBytes("42"), typeof(EndOfStreamException)),
    (Encoding.UTF8.GetBytes(new string(' ', Frames.MaximumBytes + 1) + "\n"), typeof(InvalidDataException)),
    (Encoding.UTF8.GetBytes(new string('[', 33) + "0" + new string(']', 33) + "\n"), typeof(JsonException)),
    (new byte[] { 34, 255, 34, 10 }, typeof(InvalidDataException))
})
        {
            using var stream = new MemoryStream(data);
            using var frames = new Frames(stream);
            try
            {
                await frames.ReadAsync(default);
                throw new Exception("Expected rejection: " + expected);
            }
            catch (Exception error) when (expected.IsInstanceOfType(error)) { _checks++; }
        }
    }

    private async Task ExactCeilingAsync()
    {
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("\"" + new string('x', Frames.MaximumBytes - 2) + "\"\n")))
        {
            using (var frames = new Frames(stream))
            {
                Assert((await frames.ReadAsync(default)).GetString()!.Length == Frames.MaximumBytes - 2, "exact byte ceiling");
            }
        }

    }

    private async Task CancellationAsync()
    {
        using (var input = new CancelStream())
        {
            using (var frames = new Frames(input))
            {
                using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(20)))
                {
                    try
                    {
                        await frames.ReadAsync(cancel.Token);
                        throw new Exception("Cancellation missing");
                    }
                    catch (OperationCanceledException) { _checks++; }
                }
            }
        }

    }

    private async Task AtomicOutputAsync()
    {
        using (var output = new MemoryStream())
        {
            var value = new CallbackResultFrame("callback-result", "id", "cid", JsonSerializer.SerializeToElement(new string('x', Frames.MaximumBytes)));
            try
            {
                await Frames.WriteAsync(output, value, WireJson.Default.CallbackResultFrame, default);
                throw new Exception("Oversize output accepted");
            }
            catch (InvalidDataException) { Assert(output.Length == 0, "oversize never partially dispatched"); }
        }
    }

    private async Task DiagnosticsAsync()
    {
        var activities = new List<Activity>();
        using (var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "WeavePort.Transport",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add
        })
        {
            ActivitySource.AddActivityListener(listener);
            using var input = new ChunkStream(Encoding.UTF8.GetBytes("42\n"), 1);
            using var reader = new Frames(input);
            Assert((await reader.ReadAsync(default)).GetInt32() == 42, "diagnostics preserve result");
            using var output = new MemoryStream();
            await Frames.WriteAsync(output, new CallbackResultFrame("callback-result", "id", "cid", JsonSerializer.SerializeToElement(42)), WireJson.Default.CallbackResultFrame, default);
            Assert(activities.Count == 2 && (int)activities[0].GetTagItem("reads")! == 3, "diagnostic read count");
            Assert((int)activities[0].GetTagItem("bytes")! == 3 && (int)activities[1].GetTagItem("bytes")! == output.Length, "diagnostic byte count");
            Assert(activities.All(a => a.TagObjects.All(t => t.Value is double or int)) && activities.All(a => (double)a.GetTagItem("complete.ms")! >= 0), "diagnostics contain only numeric measurements");
        }
    }
    private void Assert(bool value, string name)
    {
        if (!value)
        {
            throw new Exception(name);
        }
        _checks++;
    }
}
