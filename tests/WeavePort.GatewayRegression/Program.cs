if (args.Length == 0)
{
    throw new ArgumentException("Expected output path and optional --runtime-control.");
}
await GatewayRegression.RunAsync(args[0], args.Contains("--runtime-control"));
