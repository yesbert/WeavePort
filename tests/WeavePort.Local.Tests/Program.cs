using System.Text.Json;
using WeavePort.LocalTools;

if (args.Length == 1 && args[0] == "setup")
{
    var portable = new LocalConfiguration("", LocalConfiguration.Find("python3"), LocalConfiguration.Find("node"),
        "plugins/csharp/WeavePort.SamplePlugin" + (OperatingSystem.IsWindows() ? ".exe" : ""),
        "plugins/python/worker.py", "plugins/typescript/worker.ts", "state", true);
    await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "local-config.json"), JsonSerializer.Serialize(portable, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("Portable prerequisites configured. Run doctor local-config.json to verify them.");
    return;
}
if (args.Length == 3 && args[0] == "configure")
{
    string root = Path.GetFullPath(args[1]);
    var configuration = new LocalConfiguration(LocalConfiguration.Find("dotnet"), LocalConfiguration.Find("python3"), LocalConfiguration.Find("node"),
        Path.Combine(root, "artifacts/local/csharp/WeavePort.SamplePlugin.dll"), Path.Combine(root, "plugins/python/worker.py"),
        Path.Combine(root, "plugins/typescript/worker.ts"), Path.Combine(root, "artifacts/local/workspaces"));
    await File.WriteAllTextAsync(args[2], JsonSerializer.Serialize(configuration, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("Local fixture configuration written.");
    return;
}
if (args.Length < 2)
{
    throw new ArgumentException("Use doctor|verify|capacity CONFIG [OUTPUT] or configure ROOT CONFIG");
}

string output = Path.GetFullPath(args.Length > 2 ? args[2] : "local-evidence");
Directory.CreateDirectory(output);
LocalConfiguration config = LocalConfiguration.Load(args[1]);
switch (args[0])
{
    case "doctor":
        Environment.ExitCode = await LocalVerification.DoctorAsync(config, output);
        break;
    case "verify":
        Environment.ExitCode = await LocalVerification.RunAsync(config, output);
        break;
    case "compare-linux":
        Environment.ExitCode = await LinuxComparison.RunAsync(config, output);
        break;
    case "capacity":
        Environment.ExitCode = await LocalCapacity.RunAsync(config, output);
        break;
    default:
        throw new ArgumentException("Unknown local command");
}
