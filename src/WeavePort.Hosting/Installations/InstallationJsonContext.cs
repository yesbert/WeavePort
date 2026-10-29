using System.Text.Json.Serialization;

namespace WeavePort.Hosting;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(InstalledPluginCatalog.Manifest))]
[JsonSerializable(typeof(CompatibilityDeclaration))]
[JsonSerializable(typeof(FrameworkRequirement[]))]
internal sealed partial class InstallationJsonContext : JsonSerializerContext
{
}
