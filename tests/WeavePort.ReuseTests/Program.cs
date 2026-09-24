if (args.Length > 1 && args[0] == "--raw") { RawFixture.Run(args[1]); return; }
if (args.Contains("--worker")) { await WorkerFixture.RunAsync(); return; }
await new ReuseChecks().RunAsync(args);
