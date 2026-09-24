using System.Text.Json;

internal sealed class SchedulingWorker(bool alternate)
{
    private int _counter;

    internal static async Task RunAsync(string[] args)
    {
        await WaitForReadyGateAsync(args);
        if (args.Contains("--stderr-flood"))
        {
            FloodStderr();
        }
        Console.WriteLine("{\"type\":\"ready\",\"protocol\":1,\"pluginVersion\":\"1\"}");
        var worker = new SchedulingWorker(args.Contains("--alternate"));
        while (await Console.In.ReadLineAsync() is { } line)
        {
            await worker.ReplyAsync(line);
        }
    }

    private static async Task WaitForReadyGateAsync(string[] args)
    {
        int gate = Array.IndexOf(args, "--ready-gate");
        if (gate < 0)
        {
            return;
        }
        await File.WriteAllTextAsync(args[gate + 1] + ".pending", Environment.ProcessId.ToString());
        File.Move(args[gate + 1] + ".pending", args[gate + 1] + ".entered", overwrite: true);
        while (!File.Exists(args[gate + 1]))
        {
            await Task.Delay(10);
        }
    }

    private static void FloodStderr()
    {
        Console.Error.WriteLine(new string('x', 20000));
        for (int line = 0; line < 1000; line++)
        {
            Console.Error.WriteLine(new string('y', 1000));
        }
    }

    private async Task ReplyAsync(string line)
    {
        using var doc = JsonDocument.Parse(line);
        var frame = doc.RootElement;
        string id = frame.GetProperty("id").GetString()!;
        string operation = frame.GetProperty("operation").GetString()!;
        if (operation == "hold")
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                type = "callback",
                id,
                callbackId = "1",
                operation = "hold",
                payload = new
                {
                }
            }));
            await Console.In.ReadLineAsync();
        }
        if (operation == "hang")
        {
            await Task.Delay(Timeout.Infinite);
        }

        if (operation == "crash")
        {
            Environment.Exit(17);
        }

        object value = operation == "payload" ? frame.GetProperty("payload") :
                    new
                    {
                        tenant = frame.GetProperty("context").GetProperty("tenant").GetString(),
                        counter = ++_counter,
                        profile = alternate ? "alternate" : "default"
                    };
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            type = "result",
            id,
            value
        }));
    }
}
