if (args.Contains("--crash-stress"))
{
    await CrashStressChecks.RunAsync();
    return;
}

await ClientOperationChecks.RunAsync();

await SdkVerification.RunAsync();
