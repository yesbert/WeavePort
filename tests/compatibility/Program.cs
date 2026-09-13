using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk;
using WeavePort.Sdk.Client;

string root = Path.GetFullPath(args[0]);
string work = Path.Combine(root, "artifacts", "compatibility");
Directory.CreateDirectory(work);
var policy = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "compatibility/local-v1.json")))!;
Assembly[] assemblies = [typeof(PluginContext).Assembly, typeof(PluginHost).Assembly, typeof(IPluginClient).Assembly, typeof(PluginApplication).Assembly];
int checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new InvalidDataException("FAIL: " + label);
    checks++;
    Console.WriteLine("PASS: " + label);
}
foreach (var assembly in assemblies)
{
    string name = assembly.GetName().Name!;
    string expected = name == "WeavePort.Sdk" ? policy["AuthorSdks"]!["dotnet"]!["Version"]!.GetValue<string>() : policy["HostPackages"]![name]!.GetValue<string>();
    string actual = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
    Check(actual == expected, name + " loaded package version matches matrix");
}
using (var stream = typeof(PluginHost).Assembly.GetManifestResourceStream("WeavePort.LocalCompatibility")!)
{
    Check(JsonNode.DeepEquals(JsonNode.Parse(stream), policy), "packed Hosting embeds the reviewed compatibility matrix");
}
var lines = new List<string> { "# Reviewed .NET core API surface: signatures, parameter names and defaults" };
foreach (var assembly in assemblies.OrderBy(a => a.GetName().Name, StringComparer.Ordinal))
{
    foreach (var type in assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
    {
        lines.Add($"type {type.FullName} : {type.BaseType} [{type.Attributes & (TypeAttributes.VisibilityMask | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Interface)}]");
        foreach (var iface in type.GetInterfaces().Select(t => t.ToString()).Order(StringComparer.Ordinal)) lines.Add("  implements " + iface);
        foreach (string member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(Visible).Select(Describe).Order(StringComparer.Ordinal)) lines.Add("  " + member);
    }
}
string snapshot = string.Join('\n', lines) + "\n";
File.WriteAllText(Path.Combine(work, "api-actual.txt"), snapshot);
if (args.Length > 1 && args[1] == "--candidate")
{
    Console.WriteLine("API candidate written for explicit review; baseline was not modified and API check was not passed.");
    return;
}
string baseline = args.Length == 3 && args[1] == "--baseline" ? args[2] : Path.Combine(root, "compatibility/public-api.txt");
Check(File.ReadAllText(baseline) == snapshot,
    "packed public/protected API matches reviewed baseline (see artifacts/compatibility/api-actual.txt on drift)");
Console.WriteLine($"Verification passed: {checks} package/API assertions.");

static bool Visible(MemberInfo member) => member switch
{
    MethodBase method => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly,
    FieldInfo field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly,
    PropertyInfo property => property.GetAccessors(true).Any(a => a.IsPublic || a.IsFamily || a.IsFamilyOrAssembly),
    EventInfo item => item.AddMethod is { IsPublic: true } or { IsFamily: true } or { IsFamilyOrAssembly: true },
    Type type => type.IsNestedPublic || type.IsNestedFamily || type.IsNestedFamORAssem,
    _ => false
};
static string Describe(MemberInfo member)
{
    string result = member.MemberType + " " + member;
    if (member is MethodBase method)
    {
        result += " [" + (method.Attributes & (MethodAttributes.MemberAccessMask | MethodAttributes.Static | MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.Final)) + "]";
        result += " (" + string.Join(", ", method.GetParameters().Select(p => p.Name + (p.HasDefaultValue ? "=" + Format(p.RawDefaultValue) : ""))) + ")";
    }
    if (member is FieldInfo { IsLiteral: true } field) result += "=" + Format(field.GetRawConstantValue());
    return result;
}
static string Format(object? value) => value is null ? "null" : value is string text ? System.Text.Json.JsonSerializer.Serialize(text) : Convert.ToString(value, CultureInfo.InvariantCulture)!;
