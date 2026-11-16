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
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Randactyl.NullObliviousAnalyzer.CodeFixes;

/// <summary>
/// Handles null where an oblivious value arrives as an expression (a call, a member read, an element read):
/// return early from a local declaration, use ?., add ?? throw, or assert with !.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ExpressionCodeFixProvider)), Shared]
public sealed class ExpressionCodeFixProvider : CodeFixProvider
{
    public const string ReturnKey = "NOA.ReturnIfNull";
    public const string ConditionalAccessKey = "NOA.ConditionalAccess";
    public const string ThrowKey = "NOA.ThrowIfNull";
    public const string AssertKey = "NOA.AssertNotNull";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
    [
        DiagnosticIds.CallResult,
        DiagnosticIds.PropertyRead,
        DiagnosticIds.FieldRead,
        DiagnosticIds.CollectionElement,
    ];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context
            .Document.GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        var model = await context
            .Document.GetSemanticModelAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (root is null || model is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            // NOA005 also sits on foreach variables and spreads; only element reads are expressions to fix here.
            if (
                root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
                    is not ExpressionSyntax expression
                || expression.Span != diagnostic.Location.SourceSpan
                || expression is MemberBindingExpressionSyntax or ElementBindingExpressionSyntax
                || (
                    diagnostic.Id == DiagnosticIds.CollectionElement
                    && expression is not ElementAccessExpressionSyntax
                )
            )
            {
                continue;
            }

            var what = Syntax.Describe(expression);

            if (ReturnGuardTarget(model, expression) is { } declaration)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Return early if the value is null",
                        ct =>
                            ReplaceAsync(
                                context.Document,
                                declaration,
                                ReturnGuard(declaration, expression, what),
                                ct
                            ),
                        ReturnKey
                    ),
                    diagnostic
                );
            }

            if (ConditionalAccessChain(model, expression) is { } chain)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Use ?. to skip the access when the value is null",
                        ct =>
                            ReplaceAsync(
                                context.Document,
                                chain,
                                WithConditionalAccess(chain, expression),
                                ct
                            ),
                        ConditionalAccessKey
                    ),
                    diagnostic
                );
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Throw if the value is null",
                    ct =>
                        ReplaceAsync(
                            context.Document,
                            expression,
                            Syntax.Parenthesize(
                                BinaryExpression(
                                    SyntaxKind.CoalesceExpression,
                                    expression.WithoutTrivia(),
                                    Syntax.ThrowExpression(what)
                                )
                            ),
                            ct
                        ),
                    ThrowKey
                ),
                diagnostic
            );

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Assert that the value is not null (!)",
                    ct =>
                        ReplaceAsync(
                            context.Document,
                            expression,
                            PostfixUnaryExpression(
                                    SyntaxKind.SuppressNullableWarningExpression,
                                    Syntax.Parenthesize(expression)
                                )
                                .WithTriviaFrom(expression),
                            ct
                        ),
                    AssertKey
                ),
                diagnostic
            );
        }
    }

    /// <summary>`X x = value;` as a statement in a function that can return without a value.</summary>
    private static LocalDeclarationStatementSyntax? ReturnGuardTarget(
        SemanticModel model,
        ExpressionSyntax expression
    ) =>
        expression.Parent
            is EqualsValueClauseSyntax
            {
                Parent: VariableDeclaratorSyntax
                {
                    Parent: VariableDeclarationSyntax
                    {
                        Variables.Count: 1,
                        Parent: LocalDeclarationStatementSyntax
                        {
                            Parent: BlockSyntax or SwitchSectionSyntax,
                            UsingKeyword.RawKind: 0,
                            IsConst: false,
                        } statement,
                    },
                },
            }
        && Syntax.CanReturnWithoutValue(model, statement)
            ? statement
            : null;

    private static StatementSyntax ReturnGuard(
        LocalDeclarationStatementSyntax declaration,
        ExpressionSyntax value,
        string what
    ) =>
        Syntax
            .Guard(
                declaration,
                value,
                declaration.Declaration.Type,
                declaration.Declaration.Variables[0].Identifier.ValueText,
                Exit.Return,
                what
            )
            .WithTriviaFrom(declaration);

    /// <summary>
    /// The member-access chain that starts at the value, when making it conditional keeps the code compiling: the
    /// chain is a call used as a statement, or its result is a reference type (which then becomes maybe-null).
    /// </summary>
    private static ExpressionSyntax? ConditionalAccessChain(
        SemanticModel model,
        ExpressionSyntax value
    )
    {
        if (
            value.Parent
                is not (
                    MemberAccessExpressionSyntax
                    {
                        RawKind: (int)SyntaxKind.SimpleMemberAccessExpression
                    }
                    or ElementAccessExpressionSyntax
                )
            || value.Parent is MemberAccessExpressionSyntax access && access.Expression != value
            || value.Parent is ElementAccessExpressionSyntax element && element.Expression != value
        )
        {
            return null;
        }

        ExpressionSyntax chain = (ExpressionSyntax)value.Parent;
        while (
            chain.Parent is InvocationExpressionSyntax invocation && invocation.Expression == chain
            || chain.Parent
                is MemberAccessExpressionSyntax
                {
                    RawKind: (int)SyntaxKind.SimpleMemberAccessExpression
                } member
                && member.Expression == chain
            || chain.Parent is ElementAccessExpressionSyntax outer && outer.Expression == chain
        )
        {
            chain = (ExpressionSyntax)chain.Parent;
        }

        // Assignment targets and ref uses cannot be conditional.
        switch (chain.Parent)
        {
            case AssignmentExpressionSyntax assignment when assignment.Left == chain:
            case ArgumentSyntax { RefKindKeyword.RawKind: not 0 }:
            case PostfixUnaryExpressionSyntax or PrefixUnaryExpressionSyntax:
                return null;
            default:
                if (
                    chain is InvocationExpressionSyntax
                    && chain.Parent is ExpressionStatementSyntax
                )
                {
                    return chain;
                }

                break;
        }

        return model.GetTypeInfo(chain).Type is { IsReferenceType: true } ? chain : null;
    }

    /// <summary>Inserts ? after the value, so `value.A.B()` becomes `value?.A.B()`.</summary>
    private static ExpressionSyntax WithConditionalAccess(
        ExpressionSyntax chain,
        ExpressionSyntax value
    )
    {
        var text = chain.ToFullString();
        var at = value.Span.End - chain.FullSpan.Start;
        return ParseExpression(text.Substring(0, at) + "?" + text.Substring(at));
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
            : await Syntax
                .FormatAsync(
                    document.WithSyntaxRoot(root.ReplaceNode(node, replacement)),
                    cancellationToken
                )
                .ConfigureAwait(false);
    }
}
