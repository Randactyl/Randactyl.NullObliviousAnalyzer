using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Simplification;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Randactyl.NullObliviousAnalyzer.CodeFixes;

/// <summary>
/// Declares a lambda parameter, override parameter or overriding property nullable, so the compiler checks its uses.
/// A lambda whose parameters have no types gets the types of the delegate written out.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ParameterCodeFixProvider)), Shared]
public sealed class ParameterCodeFixProvider : CodeFixProvider
{
    public const string Key = "NOA.DeclareNullable";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
    [DiagnosticIds.DelegateParameter, DiagnosticIds.OverrideParameter];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context
            .Document.GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            var parameter = node as ParameterSyntax ?? node.Parent as ParameterSyntax;
            if (parameter is not null)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        $"Declare '{parameter.Identifier.ValueText}' nullable",
                        ct => DeclareParameterNullableAsync(context.Document, parameter, ct),
                        Key
                    ),
                    diagnostic
                );
                continue;
            }

            // An override property is reported on its type.
            if (
                node is TypeSyntax type and not NullableTypeSyntax
                && type.Parent is BasePropertyDeclarationSyntax property
                && property.Type == type
            )
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Declare the property type nullable",
                        ct =>
                            ReplaceAsync(
                                context.Document,
                                type,
                                NullableType(type.WithoutTrivia()).WithTriviaFrom(type),
                                ct
                            ),
                        Key
                    ),
                    diagnostic
                );
            }
        }
    }

    private static async Task<Document> DeclareParameterNullableAsync(
        Document document,
        ParameterSyntax parameter,
        CancellationToken cancellationToken
    )
    {
        if (parameter.Type is { } type)
        {
            return type is NullableTypeSyntax
                ? document
                : await ReplaceAsync(
                        document,
                        type,
                        NullableType(type.WithoutTrivia()).WithTriviaFrom(type),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
        }

        // x => ... or (x, y) => ...: write out every parameter's type, and make this one nullable.
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var lambda = parameter.Ancestors().OfType<LambdaExpressionSyntax>().FirstOrDefault();
        if (
            model is null
            || lambda is null
            || model.GetSymbolInfo(lambda, cancellationToken).Symbol is not IMethodSymbol method
        )
        {
            return document;
        }

        var parameters = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => SingletonSeparatedList(simple.Parameter),
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized
                .ParameterList
                .Parameters,
            _ => default,
        };
        var typed = parameters.Select(
            (p, i) =>
            {
                var typeName = ParseTypeName(
                        method.Parameters[i].Type.ToMinimalDisplayString(model, lambda.SpanStart)
                    )
                    .WithAdditionalAnnotations(Simplifier.Annotation);
                TypeSyntax declared = p == parameter ? NullableType(typeName) : typeName;
                return p.WithType(declared.WithTrailingTrivia(Space)).WithTriviaFrom(p);
            }
        );
        var list = ParameterList(SeparatedList(typed, parameters.GetSeparators()));
        LambdaExpressionSyntax replacement = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => ParenthesizedLambdaExpression(
                    list.WithParameters(SingletonSeparatedList(list.Parameters[0].WithoutTrivia()))
                        .WithTrailingTrivia(simple.Parameter.GetTrailingTrivia()),
                    simple.Body
                )
                .WithArrowToken(simple.ArrowToken)
                .WithModifiers(simple.Modifiers)
                .WithTriviaFrom(simple),
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.WithParameterList(
                list.WithTriviaFrom(parenthesized.ParameterList)
            ),
            _ => lambda,
        };
        return await ReplaceAsync(document, lambda, replacement, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<Document> ReplaceAsync(
        Document document,
        SyntaxNode node,
        SyntaxNode replacement,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        return root is null
            ? document
            : document.WithSyntaxRoot(root.ReplaceNode(node, replacement));
    }
}
