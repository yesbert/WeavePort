using System.Globalization;
using System.Reflection;
using WeavePort.Composition;
using WeavePort.Sdk.Gateway;

internal static class ApiChecks
{
    internal static void Run(string root, bool candidate)
    {
        Assembly[] assemblies = [typeof(ResultScope).Assembly, typeof(RemotePluginClient).Assembly, typeof(GatewayService).Assembly];
        var lines = new List<string> { "# Reviewed optional package API including generated gateway protocol" };
        foreach (var assembly in assemblies.OrderBy(a => a.GetName().Name, StringComparer.Ordinal))
        {
            Check.That(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0] == "0.7.0", assembly.GetName().Name + " loaded version");
            foreach (var type in assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                lines.Add($"type {type.FullName} : {type.BaseType} [{type.Attributes & (TypeAttributes.VisibilityMask | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Interface)}]");
                foreach (var iface in type.GetInterfaces().Select(t => t.ToString()).Order(StringComparer.Ordinal)) lines.Add("  implements " + iface);
                foreach (string member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(Visible).Select(Describe).Order(StringComparer.Ordinal)) lines.Add("  " + member);
            }
        }
        string snapshot = string.Join('\n', lines) + "\n";
        File.WriteAllText(Path.Combine(root, "artifacts/optional/api-actual.txt"), snapshot);
        if (candidate) { Console.WriteLine("Candidate only: API baseline was not accepted."); return; }
        Check.That(snapshot == File.ReadAllText(Path.Combine(root, "compatibility/optional-api.txt")), "packed optional API matches reviewed baseline");
    }
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

}
