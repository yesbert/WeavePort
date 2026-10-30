using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using WeavePort.Hosting;

internal static class InstallationJsonChecks
{
    private static readonly JsonSerializerOptions PreviousOptions = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter<WorkerReusePolicy>() }
    };
    private static readonly JsonSerializerOptions RequirementOptions = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    internal static void AssertCompatible(byte[] bytes)
    {
        var manifest = JsonSerializer.Deserialize<InstalledPluginCatalog.Manifest>(bytes, PreviousOptions)!;
        if (!bytes.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(manifest, PreviousOptions)))
        {
            throw new Exception("Source generation changed sealed manifest bytes relative to the previous serializer.");
        }

        string requirement = manifest.Runtimes!["dotnet"].Requirement;
        var frameworks = JsonSerializer.Deserialize<FrameworkRequirement[]>(requirement, PreviousOptions)!;
        if (requirement != JsonSerializer.Serialize(frameworks, RequirementOptions))
        {
            throw new Exception("Source generation changed normalized framework requirement bytes.");
        }
    }
}
