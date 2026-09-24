namespace WeavePort.SdkFixture;

public sealed record Configuration(Provider[] Providers, Binding[] Bindings, string Workspace, bool Socket, Dictionary<string, string> Artifacts, int MaximumConcurrentStarts = 8);
