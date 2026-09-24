using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Formatting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Options;

/// <summary>Formats whitespace without discarding deliberate wrapping or comments.</summary>
internal sealed class SourceFormatting : IDisposable
{
    private readonly AdhocWorkspace _workspace;
    private readonly OptionSet _options;

    internal SourceFormatting()
    {
        var assemblies = MefHostServices.DefaultAssemblies.Add(typeof(CSharpFormattingOptions).Assembly);
        _workspace = new AdhocWorkspace(MefHostServices.Create(assemblies));
        _options = _workspace.Options
            .WithChangedOption(FormattingOptions.NewLine, LanguageNames.CSharp, "\n")
            .WithChangedOption(FormattingOptions.UseTabs, LanguageNames.CSharp, false)
            .WithChangedOption(FormattingOptions.IndentationSize, LanguageNames.CSharp, 4)
            .WithChangedOption(CSharpFormattingOptions.WrappingKeepStatementsOnSingleLine, false)
            .WithChangedOption(CSharpFormattingOptions.WrappingPreserveSingleLine, false);
    }

    internal string Format(SyntaxNode syntax)
    {
        SyntaxNode withBraces = new Braces().Visit(syntax)!;
        string formatted = Formatter.Format(withBraces, _workspace, _options).ToFullString();
        return CompactAutoProperties(formatted).TrimEnd() + "\n";
    }

    private static string CompactAutoProperties(string source)
    {
        SyntaxNode root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var changes = root.DescendantNodes().OfType<AccessorListSyntax>()
            .Where(IsSimpleAutoProperty)
            .Select(list => new TextChange(TextSpan.FromBounds(list.OpenBraceToken.GetPreviousToken().Span.End, list.Span.End), " { " + string.Join(" ", list.Accessors.Select(
                accessor => accessor.NormalizeWhitespace().ToFullString())) + " }"));
        return SourceText.From(source).WithChanges(changes).ToString();
    }

    private static bool IsSimpleAutoProperty(AccessorListSyntax list) =>
        list.Parent is PropertyDeclarationSyntax &&
        list.Accessors.All(accessor => accessor.Body is null && accessor.ExpressionBody is null &&
            accessor.AttributeLists.Count == 0 && !accessor.SemicolonToken.IsMissing) &&
        !list.DescendantTrivia().Concat(list.OpenBraceToken.GetPreviousToken().TrailingTrivia).Any(trivia => !trivia.IsKind(SyntaxKind.WhitespaceTrivia) &&
            !trivia.IsKind(SyntaxKind.EndOfLineTrivia));

    internal void VerifyFixtures()
    {
        const string source = """
            class Example
            {
                void Run()
                {
                    // Explain the decision.
                    if (first ||
                        second) Act();

                    foreach (var (key, value) in entries) Use(key, value);
                }
            }
            """;
        const string compact = "class Example { public int Count { get; private set; } void Reset() { Count = 0; } }";
        string expanded = Format(CSharpSyntaxTree.ParseText(compact).GetRoot());
        if (!expanded.Contains("Count { get; private set; }") || expanded.Contains("{ Count = 0; }"))
        {
            throw new InvalidDataException("Executable blocks must expand while auto-properties remain compact.\n" + expanded);
        }

        string formatted = Format(CSharpSyntaxTree.ParseText(source).GetRoot());
        if (!formatted.Contains("first ||\n") ||
            !formatted.Contains("// Explain the decision.") ||
            !formatted.Contains("var (key, value) in entries"))
        {
            throw new InvalidDataException("Formatting lost intentional wrapping, comments or tuple spacing.");
        }

        if (formatted != Format(CSharpSyntaxTree.ParseText(formatted).GetRoot()))
        {
            throw new InvalidDataException("Source formatting is not idempotent.");
        }
    }

    public void Dispose() => _workspace.Dispose();
}
