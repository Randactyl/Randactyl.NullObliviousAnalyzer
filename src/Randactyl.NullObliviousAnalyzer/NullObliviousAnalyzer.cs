using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Randactyl.NullObliviousAnalyzer;

/// <summary>
/// Inventories the places where a null-oblivious reference value enters nullable-enabled code.
/// Deliberately no flow analysis: a null check on the next line does not silence a report.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NullObliviousAnalyzer : DiagnosticAnalyzer
{
    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.MinimallyQualifiedFormat;
    private static readonly SymbolDisplayFormat MemberFormat = SymbolDisplayFormat.CSharpShortErrorMessageFormat;

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        Descriptors.CallResult, Descriptors.PropertyRead, Descriptors.FieldRead, Descriptors.RefOrOutArgument,
        Descriptors.CollectionElement, Descriptors.DelegateParameter, Descriptors.OverrideParameter,
        Descriptors.NestedOblivious);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var analysis = new Analysis(start.Compilation, new ExclusionSettings(start.Options.AnalyzerConfigOptionsProvider));
            start.RegisterOperationAction(analysis.AnalyzeInvocation, OperationKind.Invocation);
            start.RegisterOperationAction(analysis.AnalyzeAwait, OperationKind.Await);
            start.RegisterOperationAction(analysis.AnalyzeOperator,
                OperationKind.Binary, OperationKind.Unary, OperationKind.Conversion,
                OperationKind.Increment, OperationKind.Decrement, OperationKind.CompoundAssignment);
            start.RegisterOperationAction(analysis.AnalyzePropertyReference, OperationKind.PropertyReference);
            start.RegisterOperationAction(analysis.AnalyzeFieldReference, OperationKind.FieldReference);
            start.RegisterOperationAction(analysis.AnalyzeArgument, OperationKind.Argument);
            start.RegisterOperationAction(analysis.AnalyzeArrayElement, OperationKind.ArrayElementReference);
            start.RegisterOperationAction(analysis.AnalyzeLoop, OperationKind.Loop);
            start.RegisterOperationAction(analysis.AnalyzeLambda, OperationKind.AnonymousFunction);
            start.RegisterOperationAction(analysis.AnalyzeMethodGroup, OperationKind.MethodReference);
            start.RegisterSymbolAction(analysis.AnalyzeType, SymbolKind.NamedType);
        });
    }

    private sealed class Analysis(Compilation compilation, ExclusionSettings settings)
    {
        // ---- NOA001: results ----

        public void AnalyzeInvocation(OperationAnalysisContext context)
        {
            var invocation = (IInvocationOperation)context.Operation;
            var method = invocation.TargetMethod;
            ReportValue(context, invocation, method.ReturnType, method.ReturnNullableAnnotation, method,
                Descriptors.CallResult, method.ToDisplayString(MemberFormat));
        }

        public void AnalyzeOperator(OperationAnalysisContext context)
        {
            var method = context.Operation switch
            {
                IBinaryOperation binary => binary.OperatorMethod,
                IUnaryOperation unary => unary.OperatorMethod,
                IConversionOperation { Conversion.IsUserDefined: true } conversion => conversion.OperatorMethod,
                IIncrementOrDecrementOperation step => step.OperatorMethod,
                ICompoundAssignmentOperation compound => compound.OperatorMethod,
                _ => null,
            };
            if (method is { MethodKind: MethodKind.UserDefinedOperator or MethodKind.Conversion })
                ReportValue(context, context.Operation, method.ReturnType, method.ReturnNullableAnnotation, method,
                    Descriptors.CallResult, method.ToDisplayString(MemberFormat));
        }

        public void AnalyzeAwait(OperationAnalysisContext context)
        {
            var await = (IAwaitOperation)context.Operation;
            var (type, annotation) = OperationHelpers.AwaitResult(await);
            // The origin is the awaited call or member, not Task<T>.GetAwaiter, so exclusions follow the member used.
            ReportValue(context, await, type, annotation, OperationHelpers.OriginSymbol(await.Operation),
                Descriptors.CallResult, Describe(await));
        }

        // ---- NOA002 / NOA003: member reads ----

        public void AnalyzePropertyReference(OperationAnalysisContext context)
        {
            var reference = (IPropertyReferenceOperation)context.Operation;
            var property = reference.Property;
            if (OperationHelpers.IsWriteOnly(reference))
                return;
            ReportValue(context, reference, property.Type, property.NullableAnnotation, property,
                Descriptors.PropertyRead, property.ToDisplayString(MemberFormat));
        }

        public void AnalyzeFieldReference(OperationAnalysisContext context)
        {
            var reference = (IFieldReferenceOperation)context.Operation;
            var field = reference.Field;
            if (field.IsConst && field.ConstantValue is not null)
                return;
            if (OperationHelpers.IsWriteOnly(reference))
                return;
            ReportValue(context, reference, field.Type, field.NullableAnnotation, field,
                Descriptors.FieldRead, field.ToDisplayString(MemberFormat));
        }

        // ---- NOA004: out / ref ----

        public void AnalyzeArgument(OperationAnalysisContext context)
        {
            var argument = (IArgumentOperation)context.Operation;
            if (argument.ArgumentKind != ArgumentKind.Explicit
                || argument.Parameter is not { RefKind: RefKind.Out or RefKind.Ref } parameter)
                return;
            ReportValue(context, argument, parameter.Type, parameter.NullableAnnotation, parameter.ContainingSymbol,
                Descriptors.RefOrOutArgument, parameter.Name);
        }

        // ---- NOA005: elements ----

        public void AnalyzeArrayElement(OperationAnalysisContext context)
        {
            var element = (IArrayElementReferenceOperation)context.Operation;
            if (OperationHelpers.IsWriteOnly(element))
                return;
            var (type, annotation) = OperationHelpers.DeclaredType(element);
            ReportValue(context, element, type, annotation, OperationHelpers.OriginSymbol(element),
                Descriptors.CollectionElement, Describe(element), allowNested: false);
        }

        public void AnalyzeLoop(OperationAnalysisContext context)
        {
            if (context.Operation is not IForEachLoopOperation loop
                || loop.Syntax is not CommonForEachStatementSyntax syntax
                || OperationHelpers.ForEachVariableLocation(syntax) is not { } location)
                return;
            var (type, annotation) = OperationHelpers.ForEachElement(loop, GetModel(syntax));
            Report(context.ReportDiagnostic, location, type, annotation, OperationHelpers.OriginSymbol(loop.Collection),
                Descriptors.CollectionElement, allowNested: false, syntax is ForEachStatementSyntax single ? single.Identifier.Text : "iteration variable", null);
        }

        // ---- NOA006: lambdas and method groups ----

        public void AnalyzeLambda(OperationAnalysisContext context)
        {
            var lambda = (IAnonymousFunctionOperation)context.Operation;
            if (DelegateInvoke(lambda) is not { } invoke)
                return;
            for (var i = 0; i < Math.Min(invoke.Parameters.Length, lambda.Symbol.Parameters.Length); i++)
            {
                var parameter = lambda.Symbol.Parameters[i];
                if (parameter.NullableAnnotation == NullableAnnotation.Annotated
                    || parameter.DeclaringSyntaxReferences.IsEmpty)
                    continue;
                ReportDelegateParameter(context, invoke.Parameters[i], parameter.Name,
                    parameter.DeclaringSyntaxReferences[0].GetSyntax(context.CancellationToken).GetLocation());
            }
        }

        public void AnalyzeMethodGroup(OperationAnalysisContext context)
        {
            var group = (IMethodReferenceOperation)context.Operation;
            if (DelegateInvoke(group) is not { } invoke)
                return;
            for (var i = 0; i < Math.Min(invoke.Parameters.Length, group.Method.Parameters.Length); i++)
            {
                var parameter = group.Method.Parameters[i];
                if (parameter.NullableAnnotation != NullableAnnotation.Annotated)
                    ReportDelegateParameter(context, invoke.Parameters[i], parameter.Name, group.Syntax.GetLocation());
            }
        }

        private void ReportDelegateParameter(OperationAnalysisContext context, IParameterSymbol delegateParameter, string name, Location location) =>
            Report(context.ReportDiagnostic, location, delegateParameter.Type, delegateParameter.NullableAnnotation,
                delegateParameter.ContainingSymbol, Descriptors.DelegateParameter, allowNested: false, name, null);

        private static IMethodSymbol? DelegateInvoke(IOperation function)
        {
            var type = function.Parent is IDelegateCreationOperation or IConversionOperation ? function.Parent.Type : null;
            // Expression<Func<...>> wraps the delegate type whose parameters are bound.
            if (type is INamedTypeSymbol { Name: "Expression", TypeArguments.Length: 1 } expression)
                type = expression.TypeArguments[0];
            return (type as INamedTypeSymbol)?.DelegateInvokeMethod;
        }

        // ---- NOA007: overrides and implementations ----

        public void AnalyzeType(SymbolAnalysisContext context)
        {
            var type = (INamedTypeSymbol)context.Symbol;
            foreach (var member in type.GetMembers())
                if (member is IMethodSymbol { OverriddenMethod: { } overridden } method)
                    CheckOverride(context, method, overridden);

            foreach (var contract in type.AllInterfaces)
                foreach (var contractMember in contract.GetMembers())
                    if (contractMember is IMethodSymbol contractMethod
                        && type.FindImplementationForInterfaceMember(contractMethod) is IMethodSymbol implementation
                        && SymbolEqualityComparer.Default.Equals(implementation.ContainingType, type))
                        CheckOverride(context, implementation, contractMethod);
        }

        private void CheckOverride(SymbolAnalysisContext context, IMethodSymbol method, IMethodSymbol baseMember)
        {
            for (var i = 0; i < Math.Min(method.Parameters.Length, baseMember.Parameters.Length); i++)
            {
                var parameter = method.Parameters[i];
                var baseParameter = baseMember.Parameters[i];
                // Declaring `string?` makes the override safe even though callers of the base can pass null.
                if (parameter.NullableAnnotation == NullableAnnotation.Annotated
                    || parameter.DeclaringSyntaxReferences.IsEmpty)
                    continue;
                var location = parameter.DeclaringSyntaxReferences[0].GetSyntax(context.CancellationToken).GetLocation();
                Report(context.ReportDiagnostic, location, baseParameter.Type, baseParameter.NullableAnnotation, baseMember,
                    Descriptors.OverrideParameter, allowNested: false, parameter.Name, baseMember.ToDisplayString(MemberFormat));
            }
        }

        // ---- shared reporting ----

        private void ReportValue(OperationAnalysisContext context, IOperation operation, ITypeSymbol? type,
            NullableAnnotation annotation, ISymbol? origin, DiagnosticDescriptor oblivious, string name, bool allowNested = true)
        {
            var kind = ObliviousTypes.Classify(type, annotation);
            if (kind == Obliviousness.None || OperationHelpers.IsInsideNameof(operation))
                return;
            if (kind == Obliviousness.Nested && IsConsumedByReportedElement(operation))
                return;
            Report(context.ReportDiagnostic, operation.Syntax.GetLocation(), type, annotation, origin, oblivious, allowNested, name, null);
        }

        private void Report(Action<Diagnostic> report, Location location, ITypeSymbol? type, NullableAnnotation annotation,
            ISymbol? origin, DiagnosticDescriptor oblivious, bool allowNested, string name, string? extra)
        {
            var kind = ObliviousTypes.Classify(type, annotation);
            if (kind == Obliviousness.None || (kind == Obliviousness.Nested && !allowNested))
                return;
            if (location.SourceTree is not { } tree
                || !GetModel(tree).GetNullableContext(location.SourceSpan.Start).HasFlag(NullableContext.AnnotationsEnabled)
                || settings.IsExcluded(origin, tree))
                return;

            var descriptor = kind == Obliviousness.Oblivious ? oblivious : Descriptors.NestedOblivious;
            report(Diagnostic.Create(descriptor, location, name, type!.ToDisplayString(TypeFormat), extra));
        }

        /// <summary>
        /// A nested type that is directly awaited or iterated is covered by the NOA001 / NOA005 report on
        /// the element it yields, so NOA008 would only repeat it.
        /// </summary>
        private bool IsConsumedByReportedElement(IOperation operation)
        {
            var parent = operation.Parent;
            while (parent is IParenthesizedOperation or IConversionOperation { IsImplicit: true, Conversion.IsUserDefined: false })
                parent = parent.Parent;

            switch (parent)
            {
                case IAwaitOperation await:
                    var (awaitedType, awaitedAnnotation) = OperationHelpers.AwaitResult(await);
                    return ObliviousTypes.IsOblivious(awaitedType, awaitedAnnotation);
                case IForEachLoopOperation loop when loop.Syntax is CommonForEachStatementSyntax:
                    var (elementType, elementAnnotation) = OperationHelpers.ForEachElement(loop, GetModel(loop.Syntax.SyntaxTree));
                    return ObliviousTypes.IsOblivious(elementType, elementAnnotation);
                default:
                    return false;
            }
        }

        private SemanticModel GetModel(SyntaxNode node) => GetModel(node.SyntaxTree);

        private SemanticModel GetModel(SyntaxTree tree) => compilation.GetSemanticModel(tree);

        private static string Describe(IOperation operation)
        {
            var text = operation.Syntax.ToString();
            return text.Length <= 40 ? text : text.Substring(0, 40) + "...";
        }
    }
}
