using System.Text.Json;

if (JsonSerializer.IsReflectionEnabledByDefault)
{
    throw new Exception("This regression must run with JSON reflection disabled.");
}

AppContext.SetSwitch("Tomlyn.TomlSerializer.IsReflectionEnabledByDefault", false);
await SealingChecks.RunAsync();
Console.WriteLine("PASS reflection-free sealing, deterministic manifests, resolution and invalid-input preservation");
