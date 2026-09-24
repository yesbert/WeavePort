using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class ControlFlow
{
    public static IEnumerable<SyntaxNode> Violations(SyntaxNode root)
    {
        foreach (SyntaxNode node in root.DescendantNodes().Where(IsControl))
        {
            if (node is IfStatementSyntax && node.Parent is ElseClauseSyntax)
            {
                continue;
            }
            SyntaxNode? parent = node.Ancestors().TakeWhile(node => !IsScope(node)).FirstOrDefault(IsControl);
            if (parent is null || parent is not IfStatementSyntax && node is IfStatementSyntax guard && IsGuard(guard))
            {
                continue;
            }
            yield return node;
        }
    }

    private static bool IsScope(SyntaxNode node) => node is BaseMethodDeclarationSyntax or AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax;
    private static bool IsControl(SyntaxNode node) => node is IfStatementSyntax or ForStatementSyntax or CommonForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax;
    private static bool IsGuard(IfStatementSyntax node)
    {
        if (node.Else is not null || node.Statement.DescendantNodes().Any(IsControl))
        {
            return false;
        }
        StatementSyntax? last = node.Statement is BlockSyntax block ? block.Statements.LastOrDefault() : node.Statement;
        return last is ReturnStatementSyntax or ThrowStatementSyntax or ContinueStatementSyntax or BreakStatementSyntax
            || last is YieldStatementSyntax yieldStatement && yieldStatement.IsKind(SyntaxKind.YieldBreakStatement);
    }

    public static void VerifyFixtures()
    {
        string[] allowed = ["if(a) return; if(b) return;", "foreach(var x in xs) { if(x == null) continue; Use(x); }", "if(a) A(); else if(b) B(); else C();", "while(a) { if(b) { Close(); yield break; } Use(); }", "while(a) { System.Action f = () => { if(b) Use(); }; }"];
        string[] rejected = ["if(a) { if(b) B(); }", "foreach(var x in xs) { foreach(var y in ys) Use(x,y); }", "if(a) { foreach(var x in xs) Use(x); }", "foreach(var x in xs) { if(a) Use(x); }", "while(a) { if(b) yield return value; }", "if(a) A(); else if(b) { if(c) C(); }"];
        foreach (string source in allowed)
        {
            if (Violations(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source).GetRoot()).Any())
            {
                throw new InvalidDataException("Allowed control-flow fixture rejected: " + source);
            }
        }
        foreach (string source in rejected)
        {
            if (!Violations(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source).GetRoot()).Any())
            {
                throw new InvalidDataException("Nested control-flow fixture accepted: " + source);
            }
        }
    }
}
