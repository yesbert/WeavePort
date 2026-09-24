using System.Text.Json;

namespace WeavePort.Hosting;
internal sealed record CompatibilityDeclaration(int HostApi, int Protocol, Dictionary<string, string> HostPackages, Dictionary<string, AuthorSdk> AuthorSdks, int[]? Protocols = null);
