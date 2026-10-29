using System.Text.Json.Serialization;

namespace WeavePort.Hosting;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(InstalledPluginCatalog.Manifest))]
[JsonSerializable(typeof(CompatibilityDeclaration))]
[JsonSerializable(typeof(FrameworkRequirement[]))]
[JsonSerializable(typeof(PublisherSignature.Envelope))]
internal sealed partial class InstallationJsonContext : JsonSerializerContext
{
}
