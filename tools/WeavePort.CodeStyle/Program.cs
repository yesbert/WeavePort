using System.Text.Json;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: WeavePort.CodeStyle <repository-root> [--write] [--area=relative-directory ...]");
    return 2;
}

ControlFlow.VerifyFixtures();
using var formatting = new SourceFormatting();
formatting.VerifyFixtures();
string[] areas = args.Skip(1)
    .Where(argument => argument.StartsWith("--area=", StringComparison.Ordinal))
    .Select(argument => argument["--area=".Length..])
    .ToArray();
var audit = new SourceAudit(Path.GetFullPath(args[0]), formatting, args.Contains("--write"), areas);
audit.Run();
Console.WriteLine(JsonSerializer.Serialize(audit.Report, new JsonSerializerOptions { WriteIndented = true }));
return audit.Passed ? 0 : 1;
