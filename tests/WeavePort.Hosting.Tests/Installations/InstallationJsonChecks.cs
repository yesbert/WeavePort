using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using WeavePort.Hosting;

internal static class InstallationJsonChecks
{
    internal static void AssertCompatible(byte[] bytes)
    {
        var previousOptions = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter<WorkerReusePolicy>() }
        };
        var manifest = JsonSerializer.Deserialize<InstalledPluginCatalog.Manifest>(bytes, previousOptions)!;
        if (!bytes.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(manifest, previousOptions)))
        {
            throw new Exception("Source generation changed sealed manifest bytes relative to the previous serializer.");
        }

        string requirement = manifest.Runtimes!["dotnet"].Requirement;
        var frameworks = JsonSerializer.Deserialize<FrameworkRequirement[]>(requirement, previousOptions)!;
        var requirementOptions = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        if (requirement != JsonSerializer.Serialize(frameworks, requirementOptions))
        {
            throw new Exception("Source generation changed normalized framework requirement bytes.");
        }
    }
}
