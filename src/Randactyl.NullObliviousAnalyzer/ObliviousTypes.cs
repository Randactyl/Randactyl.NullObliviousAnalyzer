using Microsoft.CodeAnalysis;

namespace Randactyl.NullObliviousAnalyzer;

internal enum Obliviousness
{
    None,

    /// <summary>The type itself is oblivious.</summary>
    Oblivious,

    /// <summary>The type is not oblivious but a type argument, array element or tuple element is.</summary>
    Nested,
}

/// <summary>The one place that decides what "null-oblivious" means.</summary>
internal static class ObliviousTypes
{
    // The annotation is passed separately because the declaring symbol (method, property, parameter...) is the
    // reliable source; ITypeSymbol.NullableAnnotation on an expression's type can carry flow state.
    public static bool IsOblivious(ITypeSymbol? type, NullableAnnotation annotation) =>
        type is { IsReferenceType: true, TypeKind: not (TypeKind.Dynamic or TypeKind.Error) }
        && annotation == NullableAnnotation.None;

    public static Obliviousness Classify(ITypeSymbol? type, NullableAnnotation annotation)
    {
        if (type is null)
            return Obliviousness.None;
        if (IsOblivious(type, annotation))
            return Obliviousness.Oblivious;
        return ContainsOblivious(type) ? Obliviousness.Nested : Obliviousness.None;
    }

    public static bool ContainsOblivious(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return IsOblivious(array.ElementType, array.ElementNullableAnnotation)
                    || ContainsOblivious(array.ElementType);

            // Tuples are ValueTuple<...>, so their elements are type arguments here.
            case INamedTypeSymbol named:
                for (var i = 0; i < named.TypeArguments.Length; i++)
                {
                    var argument = named.TypeArguments[i];
                    if (IsOblivious(argument, named.TypeArgumentNullableAnnotations[i]) || ContainsOblivious(argument))
                        return true;
                }

                return false;

            default:
                return false;
        }
    }
}
