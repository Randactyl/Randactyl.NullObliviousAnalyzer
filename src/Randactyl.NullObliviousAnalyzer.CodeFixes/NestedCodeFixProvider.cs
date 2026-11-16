using System.Collections.Generic;
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
/// NOA009: declares the elements or type arguments of the target type nullable (Foo[] to Foo?[], List&lt;Foo&gt; to
/// List&lt;Foo?&gt;), so the compiler checks them where they are used. The analyzer passes the target's type syntax
/// as an additional location and the positions to change as paths of child indexes.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NestedCodeFixProvider)), Shared]
public sealed class NestedCodeFixProvider : CodeFixProvider
{
    public const string Key = "NOA.DeclareElementsNullable";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
    [DiagnosticIds.NestedObliviousLost];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            if (
                diagnostic.AdditionalLocations.Count == 0
                || !diagnostic.Properties.TryGetValue(
                    DiagnosticIds.LostPathsProperty,
                    out var value
                )
                || value is null
                || diagnostic.AdditionalLocations[0].SourceTree is not { } tree
                || context.Document.Project.Solution.GetDocument(tree) is not { } target
            )
            {
                continue;
            }

            var root = await tree.GetRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root.FindNode(diagnostic.AdditionalLocations[0].SourceSpan) is not TypeSyntax type)
            {
                continue;
            }

            var paths = value
                .Split(';')
                .Select(path => path.Split('.').Select(int.Parse).ToArray())
                .ToList();
            var fixedType = Annotate(type, paths, 0);
            if (fixedType is null || fixedType.IsEquivalentTo(type))
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Declare the elements of '{type.WithoutTrivia()}' nullable",
                    ct => ReplaceAsync(target, type, fixedType.WithTriviaFrom(type), ct),
                    Key
                ),
                diagnostic
            );
        }
    }

    /// <summary>
    /// Wraps the positions the paths lead to in `?`. Returns null when the syntax does not have the shape of the type
    /// (an alias, a multi-dimensional array), so no fix is offered.
    /// </summary>
    private static TypeSyntax? Annotate(TypeSyntax type, List<int[]> paths, int depth)
    {
        if (paths.Count == 0)
        {
            return type;
        }

        switch (type)
        {
            // The top-level ? is not a position; look inside it.
            case NullableTypeSyntax nullable:
                return Annotate(nullable.ElementType, paths, depth) is { } inner
                    ? nullable.WithElementType(inner)
                    : null;
            case ArrayTypeSyntax { RankSpecifiers.Count: 1 } array:
                return Child(array.ElementType, 0, paths, depth) is { } element
                    ? array.WithElementType(element)
                    : null;
            case GenericNameSyntax generic:
                var arguments = generic.TypeArgumentList.Arguments;
                for (var i = 0; i < arguments.Count; i++)
                {
                    if (Child(arguments[i], i, paths, depth) is not { } argument)
                    {
                        return null;
                    }

                    arguments = arguments.Replace(arguments[i], argument);
                }

                return generic.WithTypeArgumentList(
                    generic.TypeArgumentList.WithArguments(arguments)
                );
            case QualifiedNameSyntax { Right: GenericNameSyntax right } qualified:
                return Annotate(right, paths, depth) is SimpleNameSyntax name
                    ? qualified.WithRight(name)
                    : null;
            case AliasQualifiedNameSyntax { Name: GenericNameSyntax right } aliased:
                return Annotate(right, paths, depth) is SimpleNameSyntax aliasedName
                    ? aliased.WithName(aliasedName)
                    : null;
            case TupleTypeSyntax tuple:
                var elements = tuple.Elements;
                for (var i = 0; i < elements.Count; i++)
                {
                    // Elements after the seventh are type arguments of the nested TRest tuple: 7.0, 7.1, ...
                    var childPaths =
                        i < 7
                            ? Under(paths, depth, i)
                            :
                            [
                                .. Under(Under(paths, depth, 7), depth + 1, i - 7)
                                    .Select(p =>
                                        p.Take(depth + 1).Concat(p.Skip(depth + 2)).ToArray()
                                    ),
                            ];
                    if (Wrap(elements[i].Type, childPaths, depth + 1) is not { } elementType)
                    {
                        return null;
                    }

                    elements = elements.Replace(elements[i], elements[i].WithType(elementType));
                }

                return tuple.WithElements(elements);
            default:
                return null;
        }
    }

    private static TypeSyntax? Child(TypeSyntax child, int index, List<int[]> paths, int depth) =>
        Wrap(child, Under(paths, depth, index), depth + 1);

    /// <summary>Annotates inside the child, then wraps the child itself if a path ends at it.</summary>
    private static TypeSyntax? Wrap(TypeSyntax child, List<int[]> paths, int depth)
    {
        var inner = Annotate(child, [.. paths.Where(p => p.Length > depth)], depth);
        return inner switch
        {
            null => null,
            _ => paths.Any(p => p.Length == depth) && inner is not NullableTypeSyntax
                ? NullableType(inner.WithoutTrivia()).WithTriviaFrom(child)
                : inner,
        };
    }

    private static List<int[]> Under(List<int[]> paths, int depth, int index) =>
        [.. paths.Where(p => p.Length > depth && p[depth] == index)];

    private static async Task<Solution> ReplaceAsync(
        Document document,
        SyntaxNode node,
        SyntaxNode replacement,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        return root is null
            ? document.Project.Solution
            : document.WithSyntaxRoot(root.ReplaceNode(node, replacement)).Project.Solution;
    }
}
