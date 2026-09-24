using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>Applies mechanical checks to the explicitly documented maintained areas.</summary>
internal sealed class SourceAudit(string root, SourceFormatting formatting, bool write, string[] areas)
{
    private const int MaximumMethodCodeLines = 60;
    private const int MaximumFileLines = 350;
    private static readonly string[] Areas =
    [
        "src", "samples", "examples", "plugins", "tests", "benchmarks", "tools"
    ];
    private readonly List<object> _sizes = [];
    private readonly List<string> _formattingFailures = [];
    private readonly List<string> _controlFlowFailures = [];

    internal bool Passed => _formattingFailures.Count == 0 && _sizes.Count == 0 && _controlFlowFailures.Count == 0;
    internal object Report => new
    {
        formattingFailures = _formattingFailures,
        sizeReview = _sizes,
        controlFlowFailures = _controlFlowFailures
    };

    internal void Run()
    {
        foreach (string path in (areas.Length == 0 ? Areas : areas).SelectMany(SourceFiles).Order(StringComparer.Ordinal))
        {
            Review(path);
        }
    }

    private IEnumerable<string> SourceFiles(string area) =>
        Directory.EnumerateFiles(Path.Combine(root, area), "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar)
                .Any(part => part is "bin" or "obj"))
            .Where(path => !path.EndsWith(".g.cs", StringComparison.Ordinal));

    private void Review(string path)
    {
        string relative = Path.GetRelativePath(root, path);
        string source = File.ReadAllText(path);
        SyntaxNode syntax = CSharpSyntaxTree.ParseText(source).GetRoot();
        if (syntax.ContainsDiagnostics)
        {
            throw new InvalidDataException("Cannot format invalid source: " + relative);
        }

        string formatted = formatting.Format(syntax);
        ApplyFormatting(path, relative, source, formatted);
        SyntaxNode measured = CSharpSyntaxTree.ParseText(formatted).GetRoot();
        _controlFlowFailures.AddRange(ControlFlow.Violations(measured)
            .Select(node => $"{relative}:{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}"));
        ReviewMethodSizes(relative, measured);
        int lines = formatted.Split('\n').Length - 1;
        if (lines > MaximumFileLines)
        {
            _sizes.Add(new
            {
                file = relative,
                fileLines = lines
            });
        }
    }

    private void ApplyFormatting(string path, string relative, string source, string formatted)
    {
        if (write)
        {
            File.WriteAllText(path, formatted);
            return;
        }

        if (source != formatted)
        {
            _formattingFailures.Add(relative);
        }
    }

    private void ReviewMethodSizes(string relative, SyntaxNode measured)
    {
        foreach (BaseMethodDeclarationSyntax method in measured.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
        {
            int lines = method.DescendantTokens()
                .Select(token => token.GetLocation().GetLineSpan().StartLinePosition.Line).Distinct().Count();
            if (lines <= MaximumMethodCodeLines)
            {
                continue;
            }

            string name = method is MethodDeclarationSyntax declaration ? declaration.Identifier.Text : ".ctor";
            _sizes.Add(new
            {
                file = relative,
                method = name,
                codeLines = lines
            });
        }
    }
}
