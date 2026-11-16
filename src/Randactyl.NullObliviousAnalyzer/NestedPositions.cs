using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Randactyl.NullObliviousAnalyzer;

/// <summary>
/// Compares the positions nested in a type (array elements, type arguments, tuple elements) with the positions of the
/// type it is converted to. Foo[] and List&lt;Foo&gt; in nullable-enabled code declare their elements non-null, so an
/// oblivious element that reaches them is no longer seen by the compiler or by this analyzer.
/// </summary>
internal static class NestedPositions
{
    /// <summary>Diagnostic property with the lost paths, separated by ';'. Read by the code fix.</summary>
    public const string LostPathsProperty = "LostPaths";

    /// <summary>
    /// The positions, with their oblivious types, nested in <paramref name="source"/> that are oblivious while the matching position of
    /// <paramref name="target"/> is declared non-null. A path lists child indexes from the top of the target type,
    /// separated by '.': 0 for an array element, i for the i-th type argument or tuple element. The top level of the
    /// types is not compared.
    /// </summary>
    public static List<(string Path, ITypeSymbol Type)> LostPaths(
        ITypeSymbol source,
        ITypeSymbol target
    )
    {
        var paths = new List<(string, ITypeSymbol)>();
        Collect(source, target, "", paths);
        return paths;
    }

    private static void Collect(
        ITypeSymbol source,
        ITypeSymbol target,
        string prefix,
        List<(string, ITypeSymbol)> paths
    )
    {
        if (source is IArrayTypeSymbol array && target is IArrayTypeSymbol targetArray)
        {
            Collect(
                array.ElementType,
                array.ElementNullableAnnotation,
                targetArray.ElementType,
                targetArray.ElementNullableAnnotation,
                prefix + "0",
                paths
            );
            return;
        }

        // List<Foo> to IEnumerable<Foo>, Foo[] to IReadOnlyList<Foo>: compare through the matching base or interface.
        if (
            target is not INamedTypeSymbol { TypeArguments.Length: > 0 } named
            || Matching(source, named) is not { } match
        )
        {
            return;
        }

        for (var i = 0; i < match.TypeArguments.Length; i++)
        {
            Collect(
                match.TypeArguments[i],
                match.TypeArgumentNullableAnnotations[i],
                named.TypeArguments[i],
                named.TypeArgumentNullableAnnotations[i],
                prefix + i,
                paths
            );
        }
    }

    private static void Collect(
        ITypeSymbol source,
        NullableAnnotation sourceAnnotation,
        ITypeSymbol target,
        NullableAnnotation targetAnnotation,
        string path,
        List<(string, ITypeSymbol)> paths
    )
    {
        if (
            ObliviousTypes.IsOblivious(source, sourceAnnotation)
            && targetAnnotation == NullableAnnotation.NotAnnotated
        )
        {
            paths.Add((path, source));
        }

        Collect(source, target, path + ".", paths);
    }

    private static INamedTypeSymbol? Matching(ITypeSymbol source, INamedTypeSymbol target) =>
        Candidates(source)
            .FirstOrDefault(candidate =>
                SymbolEqualityComparer.Default.Equals(
                    candidate.OriginalDefinition,
                    target.OriginalDefinition
                )
            );

    private static IEnumerable<INamedTypeSymbol> Candidates(ITypeSymbol source)
    {
        for (
            var current = source as INamedTypeSymbol;
            current is not null;
            current = current.BaseType
        )
        {
            yield return current;
        }

        foreach (var contract in source.AllInterfaces)
        {
            yield return contract;
        }
    }
}
