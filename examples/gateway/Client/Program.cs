using System.Text.Json;
using WeavePort.Composition;
using WeavePort.Sdk.Gateway;

if (args.Length != 3) throw new ArgumentException("Usage: Client <https-endpoint> <private-credential-file> <private-result-root>");
using var bootstrap = JsonDocument.Parse(await File.ReadAllTextAsync(args[1]));
await using var client = new RemotePluginClient(new Uri(args[0]), bootstrap.RootElement.GetProperty("credential").GetString()!);
string tenant = await client.GetTenantAsync();
// A real application compares this identity with its independently authenticated request owner.
await using var scope = new ResultScope(Path.GetFullPath(args[2]), tenant);
byte[] input = System.Text.Encoding.UTF8.GetBytes("Example bounded result\n");
var source = await scope.CreateAsync((stream, token) => stream.WriteAsync(input, token).AsTask());
var mapped = await Composition.MapAsync(scope, source, client);
await scope.CopyToAsync(mapped, Console.OpenStandardOutput());
