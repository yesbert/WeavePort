if (args.Contains("--worker"))
{
    await RegistryWorker.RunAsync();
    return;
}
if (args.Length != 2)
{
    throw new ArgumentException("Expected registration count and output directory.");
}
await RegistryRun.RunAsync(int.Parse(args[0]), args[1]);
