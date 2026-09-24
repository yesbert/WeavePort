string root = Path.GetFullPath(args[0]);
if (args.Contains("--mixed-only"))
{
    await MixedLaunchChecks.RunAsync(root);
    return;
}
await new InstallationChecks(root, args[1]).RunAsync();
