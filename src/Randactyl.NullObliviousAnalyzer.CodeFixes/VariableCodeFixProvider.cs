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
/// Handles null for a variable declared where an oblivious value arrives (out var, deconstruction, foreach): the
/// variable is renamed, and a guard right after it declares the original name as a non-null pattern variable, so
/// the code that uses it stays unchanged.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(VariableCodeFixProvider)), Shared]
public sealed class VariableCodeFixProvider : CodeFixProvider
{
    public const string ThrowKey = "NOA.GuardVariable.Throw";
    public const string ReturnKey = "NOA.GuardVariable.Return";
    public const string ContinueKey = "NOA.GuardVariable.Continue";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
    [DiagnosticIds.RefOrOutArgument, DiagnosticIds.CollectionElement];

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
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            var token = root.FindToken(diagnostic.Location.SourceSpan.Start);

            // foreach (var e in xs): the report sits on the identifier.
            if (
                token.Parent is ForEachStatementSyntax loop
                && loop.Identifier == token
                && diagnostic.Id == DiagnosticIds.CollectionElement
            )
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        $"Skip null elements in '{loop.Identifier.ValueText}'",
                        ct => GuardForEachAsync(context.Document, loop, ct),
                        ContinueKey
                    ),
                    diagnostic
                );
                continue;
            }

            if (diagnostic.Id != DiagnosticIds.RefOrOutArgument)
            {
                continue;
            }

            var designation = node switch
            {
                ArgumentSyntax
                {
                    Expression: DeclarationExpressionSyntax
                    {
                        Designation: SingleVariableDesignationSyntax single
                    }
                } => single,
                _ => token.Parent as SingleVariableDesignationSyntax,
            };
            if (designation is null)
            {
                continue;
            }

            if (
                designation.Ancestors().OfType<ForEachVariableStatementSyntax>().FirstOrDefault()
                    is { } deconstructingLoop
                && deconstructingLoop.Variable.Span.Contains(designation.Span)
            )
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        $"Skip elements where '{designation.Identifier.ValueText}' is null",
                        ct =>
                            GuardForEachDeconstructionAsync(
                                context.Document,
                                deconstructingLoop,
                                designation,
                                ct
                            ),
                        ContinueKey
                    ),
                    diagnostic
                );
                continue;
            }

            if (StatementOf(model, designation) is not { } statement)
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Throw if '{designation.Identifier.ValueText}' is null",
                    ct =>
                        GuardAfterStatementAsync(
                            context.Document,
                            statement,
                            designation,
                            Exit.Throw,
                            ct
                        ),
                    ThrowKey
                ),
                diagnostic
            );
            if (Syntax.CanReturnWithoutValue(model, statement))
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        $"Return early if '{designation.Identifier.ValueText}' is null",
                        ct =>
                            GuardAfterStatementAsync(
                                context.Document,
                                statement,
                                designation,
                                Exit.Return,
                                ct
                            ),
                        ReturnKey
                    ),
                    diagnostic
                );
            }
        }
    }

    /// <summary>
    /// The statement that declares the variable, when a guard can follow it: an expression or declaration statement
    /// in a block that does not use the variable again itself.
    /// </summary>
    private static StatementSyntax? StatementOf(
        SemanticModel model,
        SingleVariableDesignationSyntax designation
    )
    {
        var statement = designation.Ancestors().OfType<StatementSyntax>().FirstOrDefault();
        if (
            statement is not (ExpressionStatementSyntax or LocalDeclarationStatementSyntax)
            || statement.Parent is not (BlockSyntax or SwitchSectionSyntax)
            || model.GetDeclaredSymbol(designation) is not { } variable
        )
        {
            return null;
        }

        var usedAgain = statement
            .DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Any(name =>
                name.Identifier.ValueText == variable.Name
                && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(name).Symbol, variable)
            );
        return usedAgain ? null : statement;
    }

    private static async Task<Document> GuardAfterStatementAsync(
        Document document,
        StatementSyntax statement,
        SingleVariableDesignationSyntax designation,
        Exit exit,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return document;
        }

        var name = designation.Identifier.ValueText;
        var maybe = Syntax.MaybeName(model, statement, name);
        var guard = Syntax.Guard(statement, IdentifierName(maybe), null, name, exit, name);
        var renamed = statement.ReplaceToken(
            designation.Identifier,
            Identifier(maybe).WithTriviaFrom(designation.Identifier)
        );
        return await Syntax
            .FormatAsync(
                document.WithSyntaxRoot(root.ReplaceNode(statement, [renamed, guard])),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static async Task<Document> GuardForEachAsync(
        Document document,
        ForEachStatementSyntax loop,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return document;
        }

        var name = loop.Identifier.ValueText;
        var maybe = Syntax.MaybeName(model, loop.Statement, name);
        var guard = Syntax.Guard(loop, IdentifierName(maybe), loop.Type, name, Exit.Continue, name);
        var fixedLoop = loop.WithIdentifier(Identifier(maybe).WithTriviaFrom(loop.Identifier))
            .WithStatement(Prepend(loop.Statement, guard));
        return await Syntax
            .FormatAsync(
                document.WithSyntaxRoot(root.ReplaceNode(loop, fixedLoop)),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static async Task<Document> GuardForEachDeconstructionAsync(
        Document document,
        ForEachVariableStatementSyntax loop,
        SingleVariableDesignationSyntax designation,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return document;
        }

        var name = designation.Identifier.ValueText;
        var maybe = Syntax.MaybeName(model, loop.Statement, name);
        var guard = Syntax.Guard(loop, IdentifierName(maybe), null, name, Exit.Continue, name);
        var fixedLoop = loop.ReplaceToken(
            designation.Identifier,
            Identifier(maybe).WithTriviaFrom(designation.Identifier)
        );
        fixedLoop = fixedLoop.WithStatement(Prepend(fixedLoop.Statement, guard));
        return await Syntax
            .FormatAsync(
                document.WithSyntaxRoot(root.ReplaceNode(loop, fixedLoop)),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static BlockSyntax Prepend(StatementSyntax body, StatementSyntax first) =>
        body is BlockSyntax block
            ? block.WithStatements(block.Statements.Insert(0, first))
            : Block(first, body)
                .WithAdditionalAnnotations(Microsoft.CodeAnalysis.Formatting.Formatter.Annotation);
}
