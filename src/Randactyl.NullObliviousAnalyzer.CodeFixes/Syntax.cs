using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Randactyl.NullObliviousAnalyzer.CodeFixes;

internal static class DiagnosticIds
{
    public const string CallResult = "NOA001";
    public const string PropertyRead = "NOA002";
    public const string FieldRead = "NOA003";
    public const string RefOrOutArgument = "NOA004";
    public const string CollectionElement = "NOA005";
    public const string DelegateParameter = "NOA006";
    public const string OverrideParameter = "NOA007";
    public const string NestedObliviousLost = "NOA009";

    /// <summary>Same name as NestedPositions.LostPathsProperty in the analyzer.</summary>
    public const string LostPathsProperty = "LostPaths";
}

/// <summary>How a guard leaves when the value is null.</summary>
internal enum Exit
{
    Throw,
    Return,
    Continue,
}

internal static class Syntax
{
    /// <summary>throw new InvalidOperationException("&lt;what&gt; is null."), reduced to the short name where System is imported.</summary>
    public static ThrowExpressionSyntax ThrowExpression(string what) =>
        SyntaxFactory.ThrowExpression(NewException(what));

    public static StatementSyntax ExitStatement(Exit exit, string what) =>
        exit switch
        {
            Exit.Return => ReturnStatement(),
            Exit.Continue => ContinueStatement(),
            _ => ThrowStatement(NewException(what)),
        };

    /// <summary>
    /// if (&lt;value&gt; is not { } name) &lt;exit&gt;; or `is not T name` when the variable had an explicit type `T` or
    /// `T?`. Parsed from text so it stays on one line; the formatter only indents it. The line ends the way lines
    /// already end in the document that holds <paramref name="position"/>.
    /// </summary>
    public static StatementSyntax Guard(
        SyntaxNode position,
        ExpressionSyntax value,
        TypeSyntax? declaredType,
        string name,
        Exit exit,
        string what
    )
    {
        var pattern = declaredType switch
        {
            null or { IsVar: true } => "{ }",
            // A nullable type is not allowed in a pattern (CS8116); the guard only lets non-null values through.
            NullableTypeSyntax nullable => nullable.ElementType.WithoutTrivia().ToString(),
            _ => declaredType.WithoutTrivia().ToString(),
        };
        var statement = ParseStatement(
            $"if ({value.WithoutTrivia()} is not {pattern} {name}) {ExitStatement(exit, what).NormalizeWhitespace()}"
        );
        // Let the simplifier shorten global::System.InvalidOperationException where System is imported.
        statement = statement.ReplaceNodes(
            statement
                .DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Select(creation => creation.Type),
            (_, type) => type.WithAdditionalAnnotations(Simplifier.Annotation)
        );
        return statement
            .WithLeadingTrivia(ElasticMarker)
            .WithTrailingTrivia(ElasticEndOfLine(NewLine(position.SyntaxTree.GetRoot())))
            .WithAdditionalAnnotations(Formatter.Annotation);
    }

    /// <summary>
    /// Formats the nodes marked with <see cref="Formatter.Annotation"/>, breaking lines the way the document already
    /// does, and removes the mark so the code action's own cleanup, which breaks lines with the workspace's default,
    /// leaves them alone.
    /// </summary>
    public static async Task<Document> FormatAsync(
        Document document,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || !root.GetAnnotatedNodes(Formatter.Annotation).Any())
        {
            return document;
        }

        var options = (
            await document.GetOptionsAsync(cancellationToken).ConfigureAwait(false)
        ).WithChangedOption(FormattingOptions.NewLine, NewLine(root));
        document = await Formatter
            .FormatAsync(document, Formatter.Annotation, options, cancellationToken)
            .ConfigureAwait(false);
        root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        return root is null
            ? document
            : document.WithSyntaxRoot(
                root.ReplaceNodes(
                    root.GetAnnotatedNodes(Formatter.Annotation),
                    (_, node) => node.WithoutAnnotations(Formatter.Annotation)
                )
            );
    }

    /// <summary>The first line ending in the document, or the platform's when the document has none.</summary>
    private static string NewLine(SyntaxNode root) =>
        root.DescendantTrivia().FirstOrDefault(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia))
            is { RawKind: not 0 } endOfLine
            ? endOfLine.ToString()
            : System.Environment.NewLine;

    /// <summary>Wraps an expression in parentheses that the simplifier removes again where they are not needed.</summary>
    public static ExpressionSyntax Parenthesize(ExpressionSyntax expression) =>
        ParenthesizedExpression(expression.WithoutTrivia())
            .WithTriviaFrom(expression)
            .WithAdditionalAnnotations(Simplifier.Annotation);

    /// <summary>Source text of an expression on one line, for messages.</summary>
    public static string Describe(SyntaxNode node)
    {
        var text = string.Join(
            " ",
            node.ToString().Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries)
        );
        return text.Length <= 60 ? text : text.Substring(0, 60) + "...";
    }

    /// <summary>Which ways out of the enclosing function a guard can take at this position, besides throwing.</summary>
    public static bool CanReturnWithoutValue(SemanticModel model, SyntaxNode position)
    {
        foreach (var ancestor in position.Ancestors())
        {
            ISymbol? symbol = ancestor switch
            {
                AnonymousFunctionExpressionSyntax lambda => model.GetSymbolInfo(lambda).Symbol,
                LocalFunctionStatementSyntax
                or BaseMethodDeclarationSyntax
                or AccessorDeclarationSyntax => model.GetDeclaredSymbol(ancestor),
                _ => null,
            };
            if (symbol is null)
            {
                continue;
            }

            return symbol is IMethodSymbol method
                && (
                    method.ReturnsVoid
                    || (
                        method.IsAsync
                        && method.ReturnType
                            is INamedTypeSymbol { Arity: 0, Name: "Task" or "ValueTask" }
                    )
                );
        }

        return false;
    }

    /// <summary>A name for the variable that keeps holding the possibly-null value, free in the enclosing member.</summary>
    public static string MaybeName(SemanticModel model, SyntaxNode position, string name)
    {
        var candidate = name + "OrNull";
        var taken = model.LookupSymbols(position.SpanStart).Select(s => s.Name).ToList();
        var suffix = 2;
        var unique = candidate;
        while (taken.Contains(unique))
        {
            unique = candidate + suffix++;
        }

        return unique;
    }

    private static ObjectCreationExpressionSyntax NewException(string what) =>
        ObjectCreationExpression(
                ParseTypeName("global::System.InvalidOperationException")
                    .WithAdditionalAnnotations(Simplifier.Annotation)
            )
            .WithArgumentList(
                ArgumentList(
                    SingletonSeparatedList(
                        Argument(
                            LiteralExpression(
                                SyntaxKind.StringLiteralExpression,
                                Literal(what + " is null.")
                            )
                        )
                    )
                )
            );
}
