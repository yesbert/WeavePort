using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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

    public override SyntaxNode? VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
    {
        var rewritten = (ForEachVariableStatementSyntax)base.VisitForEachVariableStatement(node)!;
        return rewritten.WithStatement(Wrap(rewritten.Statement));
    }

    public override SyntaxNode? VisitDoStatement(DoStatementSyntax node)
    {
        var rewritten = (DoStatementSyntax)base.VisitDoStatement(node)!;
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
