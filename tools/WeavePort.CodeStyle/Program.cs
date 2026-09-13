using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.Json;

string root = Path.GetFullPath(args[0]);
bool write = args.Contains("--write");
string[] areas = ["src", "samples/DecisionRoom", "samples/DocumentWorkshop", "samples/AppointmentDesk", "samples/Shared"];
var sizes = new List<object>();
var failures = new List<string>();
foreach (string area in areas)
{
    foreach (string path in Directory.EnumerateFiles(Path.Combine(root, area), "*.cs", SearchOption.AllDirectories))
    {
        string relative = Path.GetRelativePath(root, path);
        if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
        {
            continue;
        }
        string source = File.ReadAllText(path);
        SyntaxNode syntax = CSharpSyntaxTree.ParseText(source).GetRoot();
        if (syntax.ContainsDiagnostics)
        {
            throw new InvalidDataException("Cannot format invalid source: " + relative);
        }
        string formatted = new Braces().Visit(syntax)!.NormalizeWhitespace(eol: "\n").ToFullString().Replace(")when (", ") when (").TrimEnd() + "\n";
        if (write)
        {
            File.WriteAllText(path, formatted);
        }
        else if (source != formatted)
        {
            failures.Add(relative);
        }
        SyntaxNode measured = CSharpSyntaxTree.ParseText(formatted).GetRoot();
        int lines = formatted.Split('\n').Length - 1;
        foreach (var method in measured.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
        {
            int count = method.DescendantTokens().Select(token => token.GetLocation().GetLineSpan().StartLinePosition.Line).Distinct().Count();
            if (count > 60)
            {
                sizes.Add(new { file = relative, method = method is MethodDeclarationSyntax declaration ? declaration.Identifier.Text : ".ctor", codeLines = count });
            }
        }
        if (lines > 350)
        {
            sizes.Add(new { file = relative, fileLines = lines });
        }
    }
}
Console.WriteLine(JsonSerializer.Serialize(new { formattingFailures = failures, sizeReview = sizes }, new JsonSerializerOptions { WriteIndented = true }));
return failures.Count == 0 && sizes.Count == 0 ? 0 : 1;

internal sealed class Braces : CSharpSyntaxRewriter
{
    private static StatementSyntax Wrap(StatementSyntax statement) => statement is BlockSyntax
        ? statement : SyntaxFactory.Block(statement);

    public override SyntaxNode? VisitIfStatement(IfStatementSyntax node)
    {
        var rewritten = (IfStatementSyntax)base.VisitIfStatement(node)!;
        return rewritten.WithStatement(Wrap(rewritten.Statement));
    }

    public override SyntaxNode? VisitElseClause(ElseClauseSyntax node)
    {
        var rewritten = (ElseClauseSyntax)base.VisitElseClause(node)!;
        return rewritten.Statement is IfStatementSyntax ? rewritten : rewritten.WithStatement(Wrap(rewritten.Statement));
    }

    public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
    {
        var rewritten = (ForStatementSyntax)base.VisitForStatement(node)!;
        return rewritten.WithStatement(Wrap(rewritten.Statement));
    }

    public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
    {
        var rewritten = (ForEachStatementSyntax)base.VisitForEachStatement(node)!;
        return rewritten.WithStatement(Wrap(rewritten.Statement));
    }

    public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node)
    {
        var rewritten = (WhileStatementSyntax)base.VisitWhileStatement(node)!;
        return rewritten.WithStatement(Wrap(rewritten.Statement));
    }

    public override SyntaxNode? VisitUsingStatement(UsingStatementSyntax node)
    {
        var rewritten = (UsingStatementSyntax)base.VisitUsingStatement(node)!;
        return rewritten.WithStatement(Wrap(rewritten.Statement));
    }

    public override SyntaxNode? VisitLockStatement(LockStatementSyntax node)
    {
        var rewritten = (LockStatementSyntax)base.VisitLockStatement(node)!;
        return rewritten.WithStatement(Wrap(rewritten.Statement));
    }
}
