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

    /// <summary>The member whose declaration decided the type of the expression, if there is one.</summary>
    public static ISymbol? OriginSymbol(IOperation operation) => Unwrap(operation) switch
    {
        IInvocationOperation invocation => invocation.TargetMethod,
        IPropertyReferenceOperation property => property.Property,
        IFieldReferenceOperation field => field.Field,
        IArrayElementReferenceOperation element => OriginSymbol(element.ArrayReference),
        _ => null,
    };

    /// <summary>
    /// The type as declared, with the annotation of the declaring symbol. IOperation.Type is avoided
    /// because the type of an expression can carry flow-adjusted nullability.
    /// </summary>
    public static (ITypeSymbol? Type, NullableAnnotation Annotation) DeclaredType(IOperation operation)
    {
        operation = Unwrap(operation);
        return operation switch
        {
            IInvocationOperation i => (i.TargetMethod.ReturnType, i.TargetMethod.ReturnNullableAnnotation),
            IPropertyReferenceOperation p => (p.Property.Type, p.Property.NullableAnnotation),
            IFieldReferenceOperation f => (f.Field.Type, f.Field.NullableAnnotation),
            ILocalReferenceOperation l => (l.Local.Type, l.Local.NullableAnnotation),
            IParameterReferenceOperation p => (p.Parameter.Type, p.Parameter.NullableAnnotation),
            IArrayElementReferenceOperation { ArrayReference: var array } when DeclaredType(array).Type is IArrayTypeSymbol arrayType
                => (arrayType.ElementType, arrayType.ElementNullableAnnotation),
            _ => (operation.Type, operation.Type?.NullableAnnotation ?? NullableAnnotation.None),
        };
    }

    /// <summary>The type produced by awaiting, read from GetAwaiter().GetResult() so type arguments keep their declared annotation.</summary>
    public static (ITypeSymbol? Type, NullableAnnotation Annotation) AwaitResult(IAwaitOperation await)
    {
        var (awaited, _) = DeclaredType(await.Operation);
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
    public static (ITypeSymbol? Type, NullableAnnotation Annotation) ForEachElement(IForEachLoopOperation loop, SemanticModel model)
    {
        if (DeclaredType(loop.Collection).Type is IArrayTypeSymbol array)
            return (array.ElementType, array.ElementNullableAnnotation);
        if (loop.Syntax is not CommonForEachStatementSyntax syntax)
            return default;

        var info = model.GetForEachStatementInfo(syntax);
        return info.CurrentProperty is { } current
            ? (current.Type, current.NullableAnnotation)
            : (info.ElementType, info.ElementType?.NullableAnnotation ?? NullableAnnotation.None);
    }

    public static Location? ForEachVariableLocation(CommonForEachStatementSyntax syntax) => syntax switch
    {
        ForEachStatementSyntax single => single.Identifier.GetLocation(),
        ForEachVariableStatementSyntax deconstructing => deconstructing.Variable.GetLocation(),
        _ => null,
    };
}
