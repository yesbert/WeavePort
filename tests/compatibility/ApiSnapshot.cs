using System.Globalization;
using System.Reflection;

internal static class ApiSnapshot
{
    internal static string Create(IEnumerable<Assembly> assemblies)
    {
        var lines = new List<string> { "# Reviewed .NET core API surface: signatures, parameter names and defaults" };
        foreach (var assembly in assemblies.OrderBy(a => a.GetName().Name, StringComparer.Ordinal))
        {
            AppendAssembly(lines, assembly);
        }
        return string.Join('\n', lines) + "\n";
    }
    private static void AppendAssembly(List<string> lines, Assembly assembly)
    {
        foreach (var type in assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            AppendType(lines, type);
        }
    }
    private static void AppendType(List<string> lines, Type type)
    {
        lines.Add($"type {type.FullName} : {type.BaseType} [{type.Attributes & (TypeAttributes.VisibilityMask | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Interface)}]");
        foreach (var iface in type.GetInterfaces().Select(t => t.ToString()).Order(StringComparer.Ordinal))
        {
            lines.Add("  implements " + iface);
        }
        foreach (string member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(Visible).Select(Describe).Order(StringComparer.Ordinal))
        {
            lines.Add("  " + member);
        }
    }
    private static bool Visible(MemberInfo member) => member switch
    {
        MethodBase method => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly,
        FieldInfo field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly,
        PropertyInfo property => property.GetAccessors(true).Any(a => a.IsPublic || a.IsFamily || a.IsFamilyOrAssembly),
        EventInfo item => item.AddMethod is { IsPublic: true } or { IsFamily: true } or { IsFamilyOrAssembly: true },
        Type type => type.IsNestedPublic || type.IsNestedFamily || type.IsNestedFamORAssem,
        _ => false
    };
    private static string Describe(MemberInfo member)
    {
        string result = member.MemberType + " " + member;
        if (member is MethodBase method)
        {
            result += " [" + (method.Attributes & (MethodAttributes.MemberAccessMask | MethodAttributes.Static | MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.Final)) + "]";
            result += " (" + string.Join(", ", method.GetParameters().Select(p => p.Name + (p.HasDefaultValue ? "=" + Format(p.RawDefaultValue) : ""))) + ")";
        }
        if (member is FieldInfo { IsLiteral: true } field)
        {
            result += "=" + Format(field.GetRawConstantValue());
        }

        return result;
    }
    private static string Format(object? value) => value is null ? "null" : value is string text ? System.Text.Json.JsonSerializer.Serialize(text) : Convert.ToString(value, CultureInfo.InvariantCulture)!;
}
