namespace WeavePort.SdkFixture;

public sealed record Ready(string Address, string[] Credentials, int Pid, Dictionary<string, string> Loaded);
