using System.Collections.Immutable;
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
    //
    // An unconstrained type parameter is neither a reference nor a value type. It counts here so that a
    // substituted type argument that is itself a type parameter of the calling code can be oblivious.
    public static bool IsOblivious(ITypeSymbol? type, NullableAnnotation annotation) =>
        type is not null
        && (
            type is { IsReferenceType: true, TypeKind: not (TypeKind.Dynamic or TypeKind.Error) }
            || type is ITypeParameterSymbol { IsValueType: false }
        )
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
                    if (
                        IsOblivious(argument, named.TypeArgumentNullableAnnotations[i])
                        || ContainsOblivious(argument)
                    )
                        return true;
                }

                return false;

            default:
                return false;
        }
    }

    // ---- declared types, resolved through the original definition ----
    //
    // Substituting a type argument into a type parameter declared without annotations loses the "oblivious"
    // marker: Box<Foo>.Value shows as a plain non-null Foo although the library can return null. The original
    // definition still says the type parameter was used with annotation None, so the declared type of a
    // member is rebuilt with those positions marked oblivious again. Everything else (foreach, await,
    // Classify) then works on the rebuilt type unchanged.

    public static (ITypeSymbol? Type, NullableAnnotation Annotation) ReturnOf(
        Compilation compilation,
        IMethodSymbol method
    )
    {
        var original = method.OriginalDefinition;
        return Resolve(
            compilation,
            method.ReturnType,
            method.ReturnNullableAnnotation,
            original.ReturnType,
            original.ReturnNullableAnnotation
        );
    }

    public static (ITypeSymbol? Type, NullableAnnotation Annotation) TypeOf(
        Compilation compilation,
        IPropertySymbol property
    )
    {
        var original = property.OriginalDefinition;
        return Resolve(
            compilation,
            property.Type,
            property.NullableAnnotation,
            original.Type,
            original.NullableAnnotation
        );
    }

    public static (ITypeSymbol? Type, NullableAnnotation Annotation) TypeOf(
        Compilation compilation,
        IFieldSymbol field
    )
    {
        var original = field.OriginalDefinition;
        return Resolve(
            compilation,
            field.Type,
            field.NullableAnnotation,
            original.Type,
            original.NullableAnnotation
        );
    }

    public static (ITypeSymbol? Type, NullableAnnotation Annotation) TypeOf(
        Compilation compilation,
        IParameterSymbol parameter
    )
    {
        var original = parameter.OriginalDefinition;
        return Resolve(
            compilation,
            parameter.Type,
            parameter.NullableAnnotation,
            original.Type,
            original.NullableAnnotation
        );
    }

    public static (ITypeSymbol? Type, NullableAnnotation Annotation) Resolve(
        Compilation compilation,
        ITypeSymbol? type,
        NullableAnnotation annotation,
        ITypeSymbol? originalType,
        NullableAnnotation originalAnnotation
    )
    {
        if (type is null || originalType is null)
            return (type, annotation);
        if (IsSubstitutedOblivious(type, annotation, originalType, originalAnnotation))
            return (type, NullableAnnotation.None);
        return (Rebuild(compilation, type, originalType), annotation);
    }

    /// <summary>
    /// The original type is a type parameter of an unannotated declaration, and what replaced it is neither a
    /// value type nor explicitly nullable.
    /// </summary>
    private static bool IsSubstitutedOblivious(
        ITypeSymbol type,
        NullableAnnotation annotation,
        ITypeSymbol original,
        NullableAnnotation originalAnnotation
    ) =>
        original is ITypeParameterSymbol
        && originalAnnotation == NullableAnnotation.None
        && annotation != NullableAnnotation.Annotated
        && !type.IsValueType
        && (
            type is { IsReferenceType: true, TypeKind: not (TypeKind.Dynamic or TypeKind.Error) }
            || type is ITypeParameterSymbol
        );

    private static ITypeSymbol Rebuild(
        Compilation compilation,
        ITypeSymbol type,
        ITypeSymbol original
    )
    {
        switch (type)
        {
            case IArrayTypeSymbol array
                when original is IArrayTypeSymbol originalArray && array.Rank == originalArray.Rank:
                var elementAnnotation = IsSubstitutedOblivious(
                    array.ElementType,
                    array.ElementNullableAnnotation,
                    originalArray.ElementType,
                    originalArray.ElementNullableAnnotation
                )
                    ? NullableAnnotation.None
                    : array.ElementNullableAnnotation;
                var element = Rebuild(compilation, array.ElementType, originalArray.ElementType);
                return
                    ReferenceEquals(element, array.ElementType)
                    && elementAnnotation == array.ElementNullableAnnotation
                    ? array
                    : compilation.CreateArrayTypeSymbol(element, array.Rank, elementAnnotation);

            case INamedTypeSymbol named
                when original is INamedTypeSymbol originalNamed
                    && named.TypeArguments.Length > 0
                    && named.TypeArguments.Length == originalNamed.TypeArguments.Length:
                var changed = false;
                var arguments = ImmutableArray.CreateBuilder<ITypeSymbol>(
                    named.TypeArguments.Length
                );
                var annotations = ImmutableArray.CreateBuilder<NullableAnnotation>(
                    named.TypeArguments.Length
                );
                for (var i = 0; i < named.TypeArguments.Length; i++)
                {
                    var argument = named.TypeArguments[i];
                    var argumentAnnotation = named.TypeArgumentNullableAnnotations[i];
                    var originalArgument = originalNamed.TypeArguments[i];
                    var originalArgumentAnnotation = originalNamed.TypeArgumentNullableAnnotations[
                        i
                    ];
                    if (
                        IsSubstitutedOblivious(
                            argument,
                            argumentAnnotation,
                            originalArgument,
                            originalArgumentAnnotation
                        )
                    )
                    {
                        changed |= argumentAnnotation != NullableAnnotation.None;
                        argumentAnnotation = NullableAnnotation.None;
                    }

                    var rebuilt = Rebuild(compilation, argument, originalArgument);
                    changed |= !ReferenceEquals(rebuilt, argument);
                    arguments.Add(rebuilt);
                    annotations.Add(argumentAnnotation);
                }

                if (!changed)
                    return named;
                if (!named.IsTupleType)
                    return named.ConstructedFrom.Construct(
                        arguments.ToImmutable(),
                        annotations.ToImmutable()
                    );
                if (named.TupleElements.Length != arguments.Count)
                    return named;

                var names = ImmutableArray.CreateBuilder<string?>(arguments.Count);
                foreach (var tupleElement in named.TupleElements)
                    names.Add(
                        tupleElement.IsExplicitlyNamedTupleElement ? tupleElement.Name : null
                    );
                return compilation.CreateTupleTypeSymbol(
                    arguments.ToImmutable(),
                    names.ToImmutable(),
                    elementNullableAnnotations: annotations.ToImmutable()
                );

            default:
                return type;
        }
    }
}
