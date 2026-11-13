using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Randactyl.NullObliviousAnalyzer;

internal static class OperationHelpers
{
    /// <summary>Plain assignment targets and out arguments are written, not read.</summary>
    public static bool IsWriteOnly(IOperation operation)
    {
        var parent = operation.Parent;
        if (parent is IArgumentOperation { Parameter.RefKind: RefKind.Out })
            return true;
        if (parent is ISimpleAssignmentOperation simple && simple.Target == operation)
            return true;

        // (a.X, b.Y) = pair
        while (parent is ITupleOperation)
        {
            operation = parent;
            parent = operation.Parent;
        }

        return parent is IDeconstructionAssignmentOperation deconstruction && deconstruction.Target == operation;
    }

    public static bool IsInsideNameof(IOperation operation)
    {
        for (var current = operation.Parent; current is not null; current = current.Parent)
            if (current is INameOfOperation)
                return true;
        return false;
    }

    /// <summary>Skips implicit conversions and parentheses so the expression that carries the declared type is found.</summary>
    public static IOperation Unwrap(IOperation operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    continue;
                case IConversionOperation { IsImplicit: true, Conversion.IsUserDefined: false } conversion:
                    operation = conversion.Operand;
                    continue;
                default:
                    return operation;
            }
        }
    }

    /// <summary>
    /// The member whose declaration decided the type of the expression, if there is one. A local with an
    /// initializer is traced to the origin of that initializer, unless the local is assigned again later.
    /// </summary>
    public static ISymbol? OriginSymbol(IOperation operation) => OriginSymbol(operation, new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default));

    private static ISymbol? OriginSymbol(IOperation operation, HashSet<ILocalSymbol> visited) => Unwrap(operation) switch
    {
        IInvocationOperation invocation => invocation.TargetMethod,
        IPropertyReferenceOperation property => property.Property,
        IFieldReferenceOperation field => field.Field,
        IArrayElementReferenceOperation element => OriginSymbol(element.ArrayReference, visited),
        IAwaitOperation await => OriginSymbol(await.Operation, visited),
        ILocalReferenceOperation local when visited.Add(local.Local) && InitializerOf(local) is { } initializer
            => OriginSymbol(initializer, visited),
        _ => null,
    };

    /// <summary>The initializer of a local that is never written again in its member, or null when that cannot be said simply.</summary>
    private static IOperation? InitializerOf(ILocalReferenceOperation reference)
    {
        var local = reference.Local;
        if (reference.SemanticModel is not { } model
            || local.DeclaringSyntaxReferences.Length != 1
            || local.DeclaringSyntaxReferences[0].GetSyntax() is not VariableDeclaratorSyntax { Initializer.Value: { } value } declarator)
            return null;

        var scope = declarator.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault();
        if (scope is null)
            return null;
        foreach (var name in scope.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (name.Identifier.ValueText != local.Name
                || model.GetOperation(name) is not ILocalReferenceOperation other
                || !SymbolEqualityComparer.Default.Equals(other.Local, local))
                continue;
            if (IsWrittenTo(other))
                return null;
        }

        // C# binds parentheses away, so there is no operation for them.
        while (value is ParenthesizedExpressionSyntax parenthesized)
            value = parenthesized.Expression;
        return model.GetOperation(value);
    }

    private static bool IsWrittenTo(IOperation reference) =>
        IsWriteOnly(reference)
        || reference.Parent switch
        {
            ICompoundAssignmentOperation compound => compound.Target == reference,
            IIncrementOrDecrementOperation => true,
            IArgumentOperation { Parameter.RefKind: RefKind.Ref } => true,
            IAddressOfOperation => true,
            _ => false,
        };

    /// <summary>
    /// The type as declared, with the annotation of the declaring symbol. IOperation.Type is avoided
    /// because the type of an expression can carry flow-adjusted nullability. Type parameters declared
    /// without annotations are resolved through the original definition (see ObliviousTypes).
    /// </summary>
    public static (ITypeSymbol? Type, NullableAnnotation Annotation) DeclaredType(Compilation compilation, IOperation operation)
    {
        operation = Unwrap(operation);
        return operation switch
        {
            IInvocationOperation i => ObliviousTypes.ReturnOf(compilation, i.TargetMethod),
            IPropertyReferenceOperation p => ObliviousTypes.TypeOf(compilation, p.Property),
            IFieldReferenceOperation f => ObliviousTypes.TypeOf(compilation, f.Field),
            ILocalReferenceOperation l => (l.Local.Type, l.Local.NullableAnnotation),
            IParameterReferenceOperation p => (p.Parameter.Type, p.Parameter.NullableAnnotation),
            IArrayElementReferenceOperation { ArrayReference: var array } when DeclaredType(compilation, array).Type is IArrayTypeSymbol arrayType
                => (arrayType.ElementType, arrayType.ElementNullableAnnotation),
            _ => (operation.Type, operation.Type?.NullableAnnotation ?? NullableAnnotation.None),
        };
    }

    /// <summary>The type produced by awaiting, read from GetAwaiter().GetResult() so type arguments keep their declared annotation.</summary>
    public static (ITypeSymbol? Type, NullableAnnotation Annotation) AwaitResult(Compilation compilation, IAwaitOperation await)
    {
        var (awaited, _) = DeclaredType(compilation, await.Operation);
        for (var type = awaited; type is not null; type = type.BaseType)
        {
            foreach (var getAwaiter in type.GetMembers("GetAwaiter"))
            {
                if (getAwaiter is not IMethodSymbol { Parameters.Length: 0 } method)
                    continue;
                foreach (var getResult in method.ReturnType.GetMembers("GetResult"))
                    if (getResult is IMethodSymbol { Parameters.Length: 0 } result)
                        return (result.ReturnType, result.ReturnNullableAnnotation);
            }
        }

        return (await.Type, await.Type?.NullableAnnotation ?? NullableAnnotation.None);
    }

    /// <summary>The element type a foreach assigns to its iteration variable, as declared by the enumerated collection.</summary>
    public static (ITypeSymbol? Type, NullableAnnotation Annotation) ForEachElement(
        Compilation compilation, IForEachLoopOperation loop, SemanticModel model)
    {
        var (collection, _) = DeclaredType(compilation, loop.Collection);
        if (collection is IArrayTypeSymbol array)
            return (array.ElementType, array.ElementNullableAnnotation);
        if (loop.Syntax is not CommonForEachStatementSyntax syntax)
            return default;

        // The compiler binds the enumerator on the substituted type. When resolving oblivious type
        // parameters changed the collection type, its Current property is stale, so look it up again.
        if (collection is not null
            && !SymbolEqualityComparer.IncludeNullability.Equals(collection, Unwrap(loop.Collection).Type)
            && ElementOf(collection, loop.IsAsynchronous) is { } resolved)
            return resolved;

        var info = model.GetForEachStatementInfo(syntax);
        return info.CurrentProperty is { } current
            ? (current.Type, current.NullableAnnotation)
            : (info.ElementType, info.ElementType?.NullableAnnotation ?? NullableAnnotation.None);
    }

    /// <summary>The element type of an enumerable type found through its GetEnumerator / Current pattern, or null if there is none.</summary>
    public static (ITypeSymbol Type, NullableAnnotation Annotation)? ElementOf(ITypeSymbol collection, bool isAsync = false)
    {
        if (collection is IArrayTypeSymbol array)
            return (array.ElementType, array.ElementNullableAnnotation);

        var getEnumerator = FindMethod(collection, isAsync ? "GetAsyncEnumerator" : "GetEnumerator");
        if (getEnumerator is null || FindProperty(getEnumerator.ReturnType, "Current") is not { } current)
            return null;
        return (current.Type, current.NullableAnnotation);
    }

    private static IMethodSymbol? FindMethod(ITypeSymbol type, string name)
    {
        foreach (var candidate in Hierarchy(type))
            foreach (var member in candidate.GetMembers(name))
                if (member is IMethodSymbol { IsStatic: false, DeclaredAccessibility: Accessibility.Public } method
                    && method.Parameters.All(p => p.IsOptional))
                    return method;
        return null;
    }

    private static IPropertySymbol? FindProperty(ITypeSymbol type, string name)
    {
        foreach (var candidate in Hierarchy(type))
            foreach (var member in candidate.GetMembers(name))
                if (member is IPropertySymbol { IsStatic: false, DeclaredAccessibility: Accessibility.Public } property)
                    return property;
        return null;
    }

    /// <summary>The type, its base types, then its interfaces with generic ones first (IEnumerable&lt;T&gt; before IEnumerable).</summary>
    private static IEnumerable<ITypeSymbol> Hierarchy(ITypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            yield return current;
        foreach (var contract in type.AllInterfaces.OrderBy(i => i.TypeArguments.Length == 0))
            yield return contract;
    }

    public static NullableAnnotation DeclaredNullableAnnotation(ISymbol symbol) => symbol switch
    {
        ILocalSymbol local => local.NullableAnnotation,
        IFieldSymbol field => field.NullableAnnotation,
        IPropertySymbol property => property.NullableAnnotation,
        IParameterSymbol parameter => parameter.NullableAnnotation,
        _ => NullableAnnotation.None,
    };

    public static Location? ForEachVariableLocation(CommonForEachStatementSyntax syntax) => syntax switch
    {
        ForEachStatementSyntax single => single.Identifier.GetLocation(),
        ForEachVariableStatementSyntax deconstructing => deconstructing.Variable.GetLocation(),
        _ => null,
    };
}
