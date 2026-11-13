using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
            start.RegisterOperationAction(analysis.AnalyzeDeconstruction, OperationKind.DeconstructionAssignment);
            start.RegisterOperationAction(analysis.AnalyzeSpread, OperationKind.Spread);
            start.RegisterOperationAction(analysis.AnalyzeLambda, OperationKind.AnonymousFunction);
            start.RegisterOperationAction(analysis.AnalyzeMethodGroup, OperationKind.MethodReference);
            start.RegisterSyntaxNodeAction(analysis.AnalyzeRangeVariable, SyntaxKind.FromClause, SyntaxKind.JoinClause);
            start.RegisterSymbolAction(analysis.AnalyzeType, SymbolKind.NamedType);
        });
    }

    private sealed class Analysis(Compilation compilation, ExclusionSettings settings)
    {
        // ---- NOA001: results ----

        public void AnalyzeInvocation(OperationAnalysisContext context)
        {
            var invocation = (IInvocationOperation)context.Operation;
            // Where / Select / OrderBy... the compiler binds for query clauses: the values they carry are
            // reported where they enter, at the range variable and at the members read from it.
            if (invocation.Syntax is QueryClauseSyntax or SelectOrGroupClauseSyntax or OrderingSyntax or QueryBodySyntax
                or QueryExpressionSyntax or QueryContinuationSyntax)
                return;
            var method = invocation.TargetMethod;
            var (type, annotation) = ObliviousTypes.ReturnOf(compilation, method);
            ReportValue(context, invocation, type, annotation, method,
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
            {
                var (type, annotation) = ObliviousTypes.ReturnOf(compilation, method);
                ReportValue(context, context.Operation, type, annotation, method,
                    Descriptors.CallResult, method.ToDisplayString(MemberFormat));
            }
        }

        public void AnalyzeAwait(OperationAnalysisContext context)
        {
            var await = (IAwaitOperation)context.Operation;
            var (type, annotation) = OperationHelpers.AwaitResult(compilation, await);
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
            // After `let` / `join`... a range variable is read through a compiler-generated anonymous type.
            if (property.ContainingType.IsAnonymousType && reference.Syntax is IdentifierNameSyntax name
                && reference.SemanticModel?.GetSymbolInfo(name, context.CancellationToken).Symbol is IRangeVariableSymbol)
                return;
            var (type, annotation) = ObliviousTypes.TypeOf(compilation, property);
            ReportValue(context, reference, type, annotation, property,
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
            var (type, annotation) = ObliviousTypes.TypeOf(compilation, field);
            ReportValue(context, reference, type, annotation, field,
                Descriptors.FieldRead, field.ToDisplayString(MemberFormat));
        }

        // ---- NOA004: out / ref ----

        public void AnalyzeArgument(OperationAnalysisContext context)
        {
            var argument = (IArgumentOperation)context.Operation;
            if (argument.ArgumentKind != ArgumentKind.Explicit
                || argument.Parameter is not { RefKind: RefKind.Out or RefKind.Ref } parameter)
                return;
            var (type, annotation) = ObliviousTypes.TypeOf(compilation, parameter);
            ReportValue(context, argument, type, annotation, parameter.ContainingSymbol,
                Descriptors.RefOrOutArgument, parameter.Name);
        }

        /// <summary>
        /// var (a, b) = x; (a, b) = x; foreach (var (a, b) in xs): the value reaches each target through an
        /// out parameter of Deconstruct. Tuples are not deconstructed through a method, so NOA008 covers them.
        /// </summary>
        public void AnalyzeDeconstruction(OperationAnalysisContext context)
        {
            if (context.Operation.Syntax is not AssignmentExpressionSyntax assignment
                || context.Operation.SemanticModel is not { } model)
                return;
            ReportDeconstruction(model, context.ReportDiagnostic, model.GetDeconstructionInfo(assignment), assignment.Left);
        }

        private void ReportDeconstruction(SemanticModel model, Action<Diagnostic> report, DeconstructionInfo info, SyntaxNode target)
        {
            var slots = DeconstructionSlots(target);
            if (slots is null)
                return;

            var outs = info.Method?.Parameters.Where(p => p.RefKind == RefKind.Out).ToArray();
            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (IsNestedTarget(slot))
                {
                    if (!info.Nested.IsDefault && i < info.Nested.Length)
                        ReportDeconstruction(model, report, info.Nested[i], slot);
                    continue;
                }

                if (outs is null || i >= outs.Length || TargetOf(model, slot) is not var (location, symbol))
                    continue;
                // `var` locals are always annotated; only an explicit `Foo?` or an existing nullable target acknowledges null.
                if (symbol is not null && OperationHelpers.DeclaredNullableAnnotation(symbol) == NullableAnnotation.Annotated
                    && slot.AncestorsAndSelf().OfType<DeclarationExpressionSyntax>().FirstOrDefault() is not { Type.IsVar: true })
                    continue;

                var parameter = outs[i];
                var (type, annotation) = ObliviousTypes.TypeOf(compilation, parameter);
                Report(report, model, location, type, annotation, info.Method, Descriptors.RefOrOutArgument,
                    allowNested: true, parameter.Name, null);
            }
        }

        private static List<SyntaxNode>? DeconstructionSlots(SyntaxNode target) => target switch
        {
            DeclarationExpressionSyntax { Designation: ParenthesizedVariableDesignationSyntax designation } => designation.Variables.Cast<SyntaxNode>().ToList(),
            ParenthesizedVariableDesignationSyntax designation => designation.Variables.Cast<SyntaxNode>().ToList(),
            TupleExpressionSyntax tuple => tuple.Arguments.Select(a => (SyntaxNode)a.Expression).ToList(),
            _ => null,
        };

        private static bool IsNestedTarget(SyntaxNode slot) => slot is
            ParenthesizedVariableDesignationSyntax or TupleExpressionSyntax
            or DeclarationExpressionSyntax { Designation: ParenthesizedVariableDesignationSyntax };

        /// <summary>The location and symbol that receives a value, or null for a discard.</summary>
        private static (Location Location, ISymbol? Symbol)? TargetOf(SemanticModel model, SyntaxNode slot)
        {
            switch (slot)
            {
                case SingleVariableDesignationSyntax single:
                    return (single.Identifier.GetLocation(), model.GetDeclaredSymbol(single));
                case DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax single }:
                    return (single.Identifier.GetLocation(), model.GetDeclaredSymbol(single));
                case DiscardDesignationSyntax or DeclarationExpressionSyntax { Designation: DiscardDesignationSyntax }:
                    return null;
                case ExpressionSyntax expression:
                    var symbol = model.GetSymbolInfo(expression).Symbol;
                    return symbol is IDiscardSymbol ? null : (expression.GetLocation(), symbol);
                default:
                    return null;
            }
        }

        // ---- NOA005: elements ----

        public void AnalyzeArrayElement(OperationAnalysisContext context)
        {
            var element = (IArrayElementReferenceOperation)context.Operation;
            if (OperationHelpers.IsWriteOnly(element))
                return;
            var (type, annotation) = OperationHelpers.DeclaredType(compilation, element);
            ReportValue(context, element, type, annotation, OperationHelpers.OriginSymbol(element),
                Descriptors.CollectionElement, Describe(element), allowNested: false);
        }

        public void AnalyzeLoop(OperationAnalysisContext context)
        {
            if (context.Operation is not IForEachLoopOperation loop
                || loop.Syntax is not CommonForEachStatementSyntax syntax
                || context.Operation.SemanticModel is not { } model)
                return;

            if (syntax is ForEachVariableStatementSyntax deconstructing)
                ReportDeconstruction(model, context.ReportDiagnostic, model.GetDeconstructionInfo(deconstructing), deconstructing.Variable);

            if (OperationHelpers.ForEachVariableLocation(syntax) is not { } location)
                return;
            var (type, annotation) = OperationHelpers.ForEachElement(compilation, loop, model);
            Report(context.ReportDiagnostic, model, location, type, annotation, OperationHelpers.OriginSymbol(loop.Collection),
                Descriptors.CollectionElement, allowNested: false, syntax is ForEachStatementSyntax single ? single.Identifier.Text : "iteration variable", null);
        }

        /// <summary>[.. xs] copies the elements of xs into the new collection.</summary>
        public void AnalyzeSpread(OperationAnalysisContext context)
        {
            var spread = (ISpreadOperation)context.Operation;
            if (spread.SemanticModel is not { } model)
                return;

            // Spreading into a collection of nullable elements is the caller acknowledging null.
            if (spread.Parent?.Type is { } target
                && OperationHelpers.ElementOf(target) is { Annotation: NullableAnnotation.Annotated })
                return;

            var (type, annotation) = SpreadElement(spread);
            Report(context.ReportDiagnostic, model, spread.Syntax.GetLocation(), type, annotation,
                OperationHelpers.OriginSymbol(spread.Operand), Descriptors.CollectionElement,
                allowNested: false, Describe(spread), null);
        }

        private (ITypeSymbol? Type, NullableAnnotation Annotation) SpreadElement(ISpreadOperation spread)
        {
            if (OperationHelpers.DeclaredType(compilation, spread.Operand).Type is { } collection
                && OperationHelpers.ElementOf(collection) is { } element)
                return element;
            return (spread.ElementType, spread.ElementType?.NullableAnnotation ?? NullableAnnotation.None);
        }

        /// <summary>from x in xs / join x in xs: the range variable receives the elements of xs like a foreach variable.</summary>
        public void AnalyzeRangeVariable(SyntaxNodeAnalysisContext context)
        {
            var (identifier, source) = context.Node switch
            {
                FromClauseSyntax from => (from.Identifier, from.Expression),
                JoinClauseSyntax join => (join.Identifier, join.InExpression),
                _ => default,
            };
            if (source is null || context.SemanticModel.GetOperation(source, context.CancellationToken) is not { } collection)
                return;

            var (collectionType, _) = OperationHelpers.DeclaredType(compilation, collection);
            if (collectionType is null || OperationHelpers.ElementOf(collectionType) is not { } element)
                return;
            Report(context.ReportDiagnostic, context.SemanticModel, identifier.GetLocation(), element.Type, element.Annotation,
                OperationHelpers.OriginSymbol(collection), Descriptors.CollectionElement, allowNested: false, identifier.Text, null);
        }

        // ---- NOA006: lambdas and method groups ----

        public void AnalyzeLambda(OperationAnalysisContext context)
        {
            var lambda = (IAnonymousFunctionOperation)context.Operation;
            // Lambdas the compiler generates for query clauses are covered by the range variable report.
            if (lambda.Syntax is not (LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
                || DelegateInvoke(lambda) is not { } invoke)
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

        private void ReportDelegateParameter(OperationAnalysisContext context, IParameterSymbol delegateParameter, string name, Location location)
        {
            var (type, annotation) = ObliviousTypes.TypeOf(compilation, delegateParameter);
            Report(context.ReportDiagnostic, context.Operation.SemanticModel, location, type, annotation,
                delegateParameter.ContainingSymbol, Descriptors.DelegateParameter, allowNested: false, name, null);
        }

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
            {
                // Accessors are checked through their property so an indexer parameter is reported once.
                if (member is IMethodSymbol { OverriddenMethod: { } overridden, MethodKind: not (MethodKind.PropertyGet or MethodKind.PropertySet) } method)
                    CheckParameters(context, method.Parameters, overridden.Parameters, overridden);
                else if (member is IPropertySymbol { OverriddenProperty: { } overriddenProperty } property)
                    CheckProperty(context, property, overriddenProperty);
            }

            foreach (var contract in type.AllInterfaces)
                foreach (var contractMember in contract.GetMembers())
                {
                    if (contractMember is IMethodSymbol { MethodKind: not (MethodKind.PropertyGet or MethodKind.PropertySet) } contractMethod
                        && type.FindImplementationForInterfaceMember(contractMethod) is IMethodSymbol implementation
                        && SymbolEqualityComparer.Default.Equals(implementation.ContainingType, type))
                        CheckParameters(context, implementation.Parameters, contractMethod.Parameters, contractMethod);
                    else if (contractMember is IPropertySymbol contractProperty
                        && type.FindImplementationForInterfaceMember(contractProperty) is IPropertySymbol implementingProperty
                        && SymbolEqualityComparer.Default.Equals(implementingProperty.ContainingType, type))
                        CheckProperty(context, implementingProperty, contractProperty);
                }
        }

        private void CheckProperty(SymbolAnalysisContext context, IPropertySymbol property, IPropertySymbol baseProperty)
        {
            CheckParameters(context, property.Parameters, baseProperty.Parameters, baseProperty);

            // Only a setter (or init) lets a value in through the property type; the override is safe if it declares it nullable.
            if (property.SetMethod is null || baseProperty.SetMethod is null
                || property.NullableAnnotation == NullableAnnotation.Annotated
                || property.DeclaringSyntaxReferences.IsEmpty)
                return;
            var typeSyntax = property.DeclaringSyntaxReferences[0].GetSyntax(context.CancellationToken) switch
            {
                PropertyDeclarationSyntax declaration => declaration.Type,
                IndexerDeclarationSyntax declaration => declaration.Type,
                _ => null,
            };
            if (typeSyntax is null)
                return;
            var (type, annotation) = ObliviousTypes.TypeOf(compilation, baseProperty);
            Report(context.ReportDiagnostic, null, typeSyntax.GetLocation(), type, annotation, baseProperty,
                Descriptors.OverrideParameter, allowNested: false, property.Name, baseProperty.ToDisplayString(MemberFormat));
        }

        private void CheckParameters(SymbolAnalysisContext context, ImmutableArray<IParameterSymbol> parameters,
            ImmutableArray<IParameterSymbol> baseParameters, ISymbol baseMember)
        {
            for (var i = 0; i < Math.Min(parameters.Length, baseParameters.Length); i++)
            {
                var parameter = parameters[i];
                // Declaring `string?` makes the override safe even though callers of the base can pass null.
                if (parameter.NullableAnnotation == NullableAnnotation.Annotated
                    || parameter.DeclaringSyntaxReferences.IsEmpty)
                    continue;
                var location = parameter.DeclaringSyntaxReferences[0].GetSyntax(context.CancellationToken).GetLocation();
                var (type, annotation) = ObliviousTypes.TypeOf(compilation, baseParameters[i]);
                Report(context.ReportDiagnostic, null, location, type, annotation, baseMember,
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
            Report(context.ReportDiagnostic, operation.SemanticModel, operation.Syntax.GetLocation(), type, annotation, origin, oblivious, allowNested, name, null);
        }

        /// <param name="model">The model of the analysis in progress; null in symbol actions, which obtain one only once a report is certain.</param>
        private void Report(Action<Diagnostic> report, SemanticModel? model, Location location, ITypeSymbol? type, NullableAnnotation annotation,
            ISymbol? origin, DiagnosticDescriptor oblivious, bool allowNested, string name, string? extra)
        {
            var kind = ObliviousTypes.Classify(type, annotation);
            if (kind == Obliviousness.None || (kind == Obliviousness.Nested && !allowNested))
                return;
            if (location.SourceTree is not { } tree
                || !(model ?? compilation.GetSemanticModel(tree)).GetNullableContext(location.SourceSpan.Start).HasFlag(NullableContext.AnnotationsEnabled)
                || settings.IsExcluded(origin, tree))
                return;

            var descriptor = kind == Obliviousness.Oblivious ? oblivious : Descriptors.NestedOblivious;
            report(Diagnostic.Create(descriptor, location, name, type!.ToDisplayString(TypeFormat), extra));
        }

        /// <summary>
        /// A nested type that is directly awaited, iterated or spread is covered by the NOA001 / NOA005 report on
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
                    var (awaitedType, awaitedAnnotation) = OperationHelpers.AwaitResult(compilation, await);
                    return ObliviousTypes.IsOblivious(awaitedType, awaitedAnnotation);
                case IForEachLoopOperation loop when loop.Syntax is CommonForEachStatementSyntax && operation.SemanticModel is { } model:
                    var (elementType, elementAnnotation) = OperationHelpers.ForEachElement(compilation, loop, model);
                    return ObliviousTypes.IsOblivious(elementType, elementAnnotation);
                case ISpreadOperation spread:
                    var (spreadType, spreadAnnotation) = SpreadElement(spread);
                    return ObliviousTypes.IsOblivious(spreadType, spreadAnnotation);
                default:
                    return false;
            }
        }

        private static string Describe(IOperation operation)
        {
            var text = operation.Syntax.ToString();
            return text.Length <= 40 ? text : text.Substring(0, 40) + "...";
        }
    }
}
