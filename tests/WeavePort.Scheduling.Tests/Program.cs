if (args.Contains("--worker"))
{
    await SchedulingWorker.RunAsync(args);
    return;
}
await SchedulingChecks.RunAsync();
