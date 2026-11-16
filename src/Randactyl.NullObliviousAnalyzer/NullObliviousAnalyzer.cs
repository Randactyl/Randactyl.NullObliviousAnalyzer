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
/// Reports the places where a null-oblivious reference value enters nullable-enabled code, unless the code that
/// receives it handles null right there. No flow analysis: a null check on the next line does not count.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NullObliviousAnalyzer : DiagnosticAnalyzer
{
    private static readonly SymbolDisplayFormat TypeFormat =
        SymbolDisplayFormat.MinimallyQualifiedFormat;
    private static readonly SymbolDisplayFormat AnnotatedTypeFormat =
        SymbolDisplayFormat.MinimallyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
        );
    private static readonly SymbolDisplayFormat MemberFormat =
        SymbolDisplayFormat.CSharpShortErrorMessageFormat;

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        Descriptors.CallResult,
        Descriptors.PropertyRead,
        Descriptors.FieldRead,
        Descriptors.RefOrOutArgument,
        Descriptors.CollectionElement,
        Descriptors.DelegateParameter,
        Descriptors.OverrideParameter,
        Descriptors.NestedObliviousLost,
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var analysis = new Analysis(
                start.Compilation,
                new ExclusionSettings(start.Options.AnalyzerConfigOptionsProvider)
            );
            start.RegisterOperationAction(analysis.AnalyzeInvocation, OperationKind.Invocation);
            start.RegisterOperationAction(analysis.AnalyzeAwait, OperationKind.Await);
            start.RegisterOperationAction(
                analysis.AnalyzeOperator,
                OperationKind.Binary,
                OperationKind.Unary,
                OperationKind.Conversion,
                OperationKind.Increment,
                OperationKind.Decrement,
                OperationKind.CompoundAssignment
            );
            start.RegisterOperationAction(
                analysis.AnalyzePropertyReference,
                OperationKind.PropertyReference
            );
            start.RegisterOperationAction(
                analysis.AnalyzeFieldReference,
                OperationKind.FieldReference
            );
            start.RegisterOperationAction(analysis.AnalyzeArgument, OperationKind.Argument);
            start.RegisterOperationAction(
                analysis.AnalyzeNestedSource,
                OperationKind.Invocation,
                OperationKind.PropertyReference,
                OperationKind.FieldReference,
                OperationKind.LocalReference,
                OperationKind.ParameterReference,
                OperationKind.ArrayElementReference,
                OperationKind.Await
            );
            start.RegisterOperationAction(
                analysis.AnalyzeArrayElement,
                OperationKind.ArrayElementReference
            );
            start.RegisterOperationAction(analysis.AnalyzeLoop, OperationKind.Loop);
            start.RegisterOperationAction(
                analysis.AnalyzeDeconstruction,
                OperationKind.DeconstructionAssignment
            );
            start.RegisterOperationAction(analysis.AnalyzeSpread, OperationKind.Spread);
            start.RegisterOperationAction(analysis.AnalyzeLambda, OperationKind.AnonymousFunction);
            start.RegisterOperationAction(
                analysis.AnalyzeMethodGroup,
                OperationKind.MethodReference
            );
            start.RegisterSyntaxNodeAction(
                analysis.AnalyzeRangeVariable,
                SyntaxKind.FromClause,
                SyntaxKind.JoinClause
            );
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
            if (IsQueryClause(invocation.Syntax))
            {
                return;
            }

            var method = invocation.TargetMethod;
            var (type, annotation) = ObliviousTypes.ReturnOf(compilation, method);
            ReportValue(
                context,
                invocation,
                type,
                annotation,
                method,
                Descriptors.CallResult,
                method.ToDisplayString(MemberFormat)
            );
        }

        public void AnalyzeOperator(OperationAnalysisContext context)
        {
            var method = context.Operation switch
            {
                IBinaryOperation binary => binary.OperatorMethod,
                IUnaryOperation unary => unary.OperatorMethod,
                IConversionOperation { Conversion.IsUserDefined: true } conversion =>
                    conversion.OperatorMethod,
                IIncrementOrDecrementOperation step => step.OperatorMethod,
                ICompoundAssignmentOperation compound => compound.OperatorMethod,
                _ => null,
            };
            if (method is { MethodKind: MethodKind.UserDefinedOperator or MethodKind.Conversion })
            {
                var (type, annotation) = ObliviousTypes.ReturnOf(compilation, method);
                ReportValue(
                    context,
                    context.Operation,
                    type,
                    annotation,
                    method,
                    Descriptors.CallResult,
                    method.ToDisplayString(MemberFormat)
                );
            }
        }

        public void AnalyzeAwait(OperationAnalysisContext context)
        {
            var await = (IAwaitOperation)context.Operation;
            var (type, annotation) = OperationHelpers.AwaitResult(compilation, await);
            // The origin is the awaited call or member, not Task<T>.GetAwaiter, so exclusions follow the member used.
            ReportValue(
                context,
                await,
                type,
                annotation,
                OperationHelpers.OriginSymbol(await.Operation),
                Descriptors.CallResult,
                Describe(await)
            );
        }

        // ---- NOA002 / NOA003: member reads ----

        public void AnalyzePropertyReference(OperationAnalysisContext context)
        {
            var reference = (IPropertyReferenceOperation)context.Operation;
            var property = reference.Property;
            if (OperationHelpers.IsWriteOnly(reference))
            {
                return;
            }
            // After `let` / `join`... a range variable is read through a compiler-generated anonymous type.
            if (
                property.ContainingType.IsAnonymousType
                && reference.Syntax is IdentifierNameSyntax name
                && reference.SemanticModel?.GetSymbolInfo(name, context.CancellationToken).Symbol
                    is IRangeVariableSymbol
            )
            {
                return;
            }

            var (type, annotation) = ObliviousTypes.TypeOf(compilation, property);
            ReportValue(
                context,
                reference,
                type,
                annotation,
                property,
                Descriptors.PropertyRead,
                property.ToDisplayString(MemberFormat)
            );
        }

        public void AnalyzeFieldReference(OperationAnalysisContext context)
        {
            var reference = (IFieldReferenceOperation)context.Operation;
            var field = reference.Field;
            if (field.IsConst && field.ConstantValue is not null)
            {
                return;
            }

            if (OperationHelpers.IsWriteOnly(reference))
            {
                return;
            }

            var (type, annotation) = ObliviousTypes.TypeOf(compilation, field);
            ReportValue(
                context,
                reference,
                type,
                annotation,
                field,
                Descriptors.FieldRead,
                field.ToDisplayString(MemberFormat)
            );
        }

        // ---- NOA009: nested oblivious positions declared non-null ----

        public void AnalyzeNestedSource(OperationAnalysisContext context)
        {
            var operation = context.Operation;
            if (
                IsQueryClause(operation.Syntax)
                || OperationHelpers.IsWriteOnly(operation)
                || OperationHelpers.IsInsideNameof(operation)
                || OperationHelpers.IsNullForgiven(operation.Syntax)
            )
            {
                return;
            }

            var (type, _) = operation is IAwaitOperation await
                ? OperationHelpers.AwaitResult(compilation, await)
                : OperationHelpers.DeclaredType(compilation, operation);
            if (type is null || !ObliviousTypes.ContainsOblivious(type))
            {
                return;
            }

            ReportNestedLost(
                context.ReportDiagnostic,
                operation.SemanticModel,
                operation.Syntax.GetLocation(),
                type,
                OperationHelpers.NestedTarget(compilation, operation),
                OperationHelpers.OriginSymbol(operation),
                Describe(operation)
            );
        }

        // ---- NOA004: out / ref ----

        public void AnalyzeArgument(OperationAnalysisContext context)
        {
            var argument = (IArgumentOperation)context.Operation;
            if (
                argument.ArgumentKind != ArgumentKind.Explicit
                || argument.Parameter is not { RefKind: RefKind.Out or RefKind.Ref } parameter
            )
            {
                return;
            }

            var (type, annotation) = ObliviousTypes.TypeOf(compilation, parameter);
            var receiver = argument.Value is IDeclarationExpressionOperation declaration
                ? declaration.Expression
                : argument.Value;
            ReportNestedLost(
                context.ReportDiagnostic,
                argument.SemanticModel,
                argument.Syntax.GetLocation(),
                type,
                OperationHelpers.DeclaredType(compilation, receiver).Type is { } received
                    ? (
                        received,
                        receiver is ILocalReferenceOperation local
                            ? OperationHelpers.TypeSyntaxOf(local.Local)
                            : null
                    )
                    : null,
                parameter.ContainingSymbol,
                argument.Value.Syntax.ToString()
            );
            // `out var x` declares the variable right where the value arrives, so it can be handled at its reads.
            if (
                ObliviousTypes.IsOblivious(type, annotation)
                && argument.Value
                    is IDeclarationExpressionOperation
                    {
                        Expression: ILocalReferenceOperation declared
                    }
                && argument.SemanticModel is { } model
                && OperationHelpers.IsNullHandledVariable(declared.Local, argument.Syntax, model)
            )
            {
                return;
            }

            ReportValue(
                context,
                argument,
                type,
                annotation,
                parameter.ContainingSymbol,
                Descriptors.RefOrOutArgument,
                argument.Value.Syntax.ToString()
            );
        }

        /// <summary>Where the value being deconstructed comes from: a tuple literal, or a value of a known type.</summary>
        private readonly struct DeconstructionSource(
            IOperation? literal,
            ITypeSymbol? type,
            NullableAnnotation annotation,
            ISymbol? origin
        )
        {
            public IOperation? Literal { get; } = literal;
            public ITypeSymbol? Type { get; } = type;
            public NullableAnnotation Annotation { get; } = annotation;
            public ISymbol? Origin { get; } = origin;
        }

        /// <summary>
        /// var (a, b) = x; (a, b) = x; foreach (var (a, b) in xs): each target receives a value, through an out
        /// parameter of Deconstruct or as a tuple element.
        /// </summary>
        public void AnalyzeDeconstruction(OperationAnalysisContext context)
        {
            if (
                context.Operation is not IDeconstructionAssignmentOperation deconstruction
                || deconstruction.Syntax is not AssignmentExpressionSyntax assignment
                || deconstruction.SemanticModel is not { } model
            )
            {
                return;
            }

            var value = OperationHelpers.Unwrap(deconstruction.Value);
            var (type, annotation) = OperationHelpers.DeclaredType(compilation, value);
            ReportDeconstruction(
                model,
                context.ReportDiagnostic,
                model.GetDeconstructionInfo(assignment),
                assignment.Left,
                new DeconstructionSource(
                    value is ITupleOperation ? value : null,
                    type,
                    annotation,
                    OperationHelpers.OriginSymbol(value)
                )
            );
        }

        private void ReportDeconstruction(
            SemanticModel model,
            Action<Diagnostic> report,
            DeconstructionInfo info,
            SyntaxNode target,
            DeconstructionSource source
        )
        {
            var slots = DeconstructionSlots(target);
            if (slots is null)
            {
                return;
            }

            var outs = info.Method?.Parameters.Where(p => p.RefKind == RefKind.Out).ToArray();
            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (ElementOf(source, outs, info.Method, i) is not { } element)
                {
                    continue;
                }

                if (IsNestedTarget(slot))
                {
                    if (!info.Nested.IsDefault && i < info.Nested.Length)
                    {
                        ReportDeconstruction(model, report, info.Nested[i], slot, element);
                    }

                    continue;
                }

                if (TargetOf(model, slot) is not var (location, name, declaration))
                {
                    continue;
                }

                var variable = declaration is not null
                    ? model.GetDeclaredSymbol(declaration)
                    : model.GetSymbolInfo(slot).Symbol;
                ReportNestedLost(
                    report,
                    model,
                    location,
                    element.Type,
                    OperationHelpers.FromSymbol(variable),
                    element.Origin,
                    name
                );
                // A tuple literal element is reported where it is produced.
                if (element.Literal is not null)
                {
                    continue;
                }

                if (
                    declaration is not null
                    && ObliviousTypes.IsOblivious(element.Type, element.Annotation)
                    && variable is not null
                    && OperationHelpers.IsNullHandledVariable(variable, declaration, model)
                )
                {
                    continue;
                }

                Report(
                    report,
                    model,
                    location,
                    element.Type,
                    element.Annotation,
                    element.Origin,
                    Descriptors.RefOrOutArgument,
                    name,
                    null
                );
            }
        }

        /// <summary>The value the i-th target receives: an out parameter of Deconstruct, or a tuple element.</summary>
        private DeconstructionSource? ElementOf(
            DeconstructionSource source,
            IParameterSymbol[]? outs,
            IMethodSymbol? method,
            int i
        )
        {
            if (outs is not null)
            {
                if (i >= outs.Length)
                {
                    return null;
                }

                var (type, annotation) = ObliviousTypes.TypeOf(compilation, outs[i]);
                return new DeconstructionSource(null, type, annotation, method);
            }

            if (source.Literal is ITupleOperation tuple)
            {
                if (i >= tuple.Elements.Length)
                {
                    return null;
                }

                var element = OperationHelpers.Unwrap(tuple.Elements[i]);
                var (type, annotation) = OperationHelpers.DeclaredType(compilation, element);
                // Literal: the element expression is reported itself, and a nested tuple literal is walked into.
                return new DeconstructionSource(
                    element,
                    type,
                    annotation,
                    OperationHelpers.OriginSymbol(element)
                );
            }

            return
                source.Type is INamedTypeSymbol { IsTupleType: true } tupleType
                && TupleElement(tupleType, i) is var (elementType, elementAnnotation)
                ? new DeconstructionSource(null, elementType, elementAnnotation, source.Origin)
                : null;
        }

        /// <summary>The i-th element of a tuple type; elements after the seventh live in the nested TRest tuple.</summary>
        private static (ITypeSymbol, NullableAnnotation)? TupleElement(
            INamedTypeSymbol tuple,
            int i
        )
        {
            while (i >= 7 && tuple.TypeArguments.Length == 8)
            {
                if (tuple.TypeArguments[7] is not INamedTypeSymbol rest)
                {
                    return null;
                }

                tuple = rest;
                i -= 7;
            }

            return i < tuple.TypeArguments.Length
                ? (tuple.TypeArguments[i], tuple.TypeArgumentNullableAnnotations[i])
                : null;
        }

        private static List<SyntaxNode>? DeconstructionSlots(SyntaxNode target) =>
            target switch
            {
                DeclarationExpressionSyntax
                {
                    Designation: ParenthesizedVariableDesignationSyntax designation
                } => [.. designation.Variables.Cast<SyntaxNode>()],
                ParenthesizedVariableDesignationSyntax designation =>
                [
                    .. designation.Variables.Cast<SyntaxNode>(),
                ],
                TupleExpressionSyntax tuple =>
                [
                    .. tuple.Arguments.Select(a => (SyntaxNode)a.Expression),
                ],
                _ => null,
            };

        private static bool IsNestedTarget(SyntaxNode slot) =>
            slot
                is ParenthesizedVariableDesignationSyntax
                    or TupleExpressionSyntax
                    or DeclarationExpressionSyntax
                    {
                        Designation: ParenthesizedVariableDesignationSyntax
                    };

        /// <summary>
        /// The location and name that receive a value, and the designation when the target declares a new variable.
        /// Null for a discard.
        /// </summary>
        private static (Location Location, string Name, SyntaxNode? Declaration)? TargetOf(
            SemanticModel model,
            SyntaxNode slot
        ) =>
            slot switch
            {
                SingleVariableDesignationSyntax single => (
                    single.Identifier.GetLocation(),
                    single.Identifier.Text,
                    single
                ),
                DeclarationExpressionSyntax
                {
                    Designation: SingleVariableDesignationSyntax single
                } => (single.Identifier.GetLocation(), single.Identifier.Text, single),
                ExpressionSyntax expression
                    when model.GetSymbolInfo(expression).Symbol is not IDiscardSymbol => (
                    expression.GetLocation(),
                    expression.ToString(),
                    null
                ),
                _ => null,
            };

        // ---- NOA005: elements ----

        public void AnalyzeArrayElement(OperationAnalysisContext context)
        {
            var element = (IArrayElementReferenceOperation)context.Operation;
            if (OperationHelpers.IsWriteOnly(element))
            {
                return;
            }

            var (type, annotation) = OperationHelpers.DeclaredType(compilation, element);
            ReportValue(
                context,
                element,
                type,
                annotation,
                OperationHelpers.OriginSymbol(element),
                Descriptors.CollectionElement,
                Describe(element)
            );
        }

        public void AnalyzeLoop(OperationAnalysisContext context)
        {
            if (
                context.Operation is not IForEachLoopOperation loop
                || loop.Syntax is not CommonForEachStatementSyntax syntax
                || context.Operation.SemanticModel is not { } model
            )
            {
                return;
            }

            var (type, annotation) = OperationHelpers.ForEachElement(compilation, loop, model);
            var origin = OperationHelpers.OriginSymbol(loop.Collection);
            if (syntax is ForEachVariableStatementSyntax deconstructing)
            {
                ReportDeconstruction(
                    model,
                    context.ReportDiagnostic,
                    model.GetDeconstructionInfo(deconstructing),
                    deconstructing.Variable,
                    new DeconstructionSource(null, type, annotation, origin)
                );
            }

            if (OperationHelpers.ForEachVariableLocation(syntax) is not { } location)
            {
                return;
            }

            var variable = syntax is ForEachStatementSyntax single
                ? model.GetDeclaredSymbol(single, context.CancellationToken)
                : null;
            if (variable is not null)
            {
                ReportNestedLost(
                    context.ReportDiagnostic,
                    model,
                    location,
                    type,
                    OperationHelpers.FromSymbol(variable),
                    origin,
                    variable.Name
                );
            }
            // A single iteration variable is declared right where the element arrives.
            if (
                variable is not null
                && ObliviousTypes.IsOblivious(type, annotation)
                && OperationHelpers.IsNullHandledVariable(variable, syntax, model)
            )
            {
                return;
            }

            Report(
                context.ReportDiagnostic,
                model,
                location,
                type,
                annotation,
                origin,
                Descriptors.CollectionElement,
                syntax is ForEachStatementSyntax named
                    ? named.Identifier.Text
                    : "iteration variable",
                null
            );
        }

        /// <summary>[.. xs] copies the elements of xs into the new collection.</summary>
        public void AnalyzeSpread(OperationAnalysisContext context)
        {
            var spread = (ISpreadOperation)context.Operation;
            if (spread.SemanticModel is not { } model)
            {
                return;
            }

            // Spreading into a collection of nullable elements is the caller acknowledging null.
            if (
                spread.Parent?.Type is { } target
                && OperationHelpers.ElementOf(target)
                    is { Annotation: NullableAnnotation.Annotated }
            )
            {
                return;
            }

            var (type, annotation) = SpreadElement(spread);
            Report(
                context.ReportDiagnostic,
                model,
                spread.Syntax.GetLocation(),
                type,
                annotation,
                OperationHelpers.OriginSymbol(spread.Operand),
                Descriptors.CollectionElement,
                Describe(spread),
                null
            );
        }

        private (ITypeSymbol? Type, NullableAnnotation Annotation) SpreadElement(
            ISpreadOperation spread
        )
        {
            if (
                OperationHelpers.DeclaredType(compilation, spread.Operand).Type is { } collection
                && OperationHelpers.ElementOf(collection) is { } element
            )
            {
                return element;
            }

            return (
                spread.ElementType,
                spread.ElementType?.NullableAnnotation ?? NullableAnnotation.None
            );
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
            if (
                source is null
                || context.SemanticModel.GetOperation(source, context.CancellationToken)
                    is not { } collection
            )
            {
                return;
            }

            var (collectionType, _) = OperationHelpers.DeclaredType(compilation, collection);
            if (
                collectionType is null
                || OperationHelpers.ElementOf(collectionType) is not { } element
            )
            {
                return;
            }

            Report(
                context.ReportDiagnostic,
                context.SemanticModel,
                identifier.GetLocation(),
                element.Type,
                element.Annotation,
                OperationHelpers.OriginSymbol(collection),
                Descriptors.CollectionElement,
                identifier.Text,
                null
            );
        }

        // ---- NOA006: lambdas and method groups ----

        public void AnalyzeLambda(OperationAnalysisContext context)
        {
            var lambda = (IAnonymousFunctionOperation)context.Operation;
            // Lambdas the compiler generates for query clauses are covered by the range variable report.
            if (
                lambda.Syntax is not (LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
                || DelegateInvoke(lambda) is not { } invoke
            )
            {
                return;
            }

            for (
                var i = 0;
                i < Math.Min(invoke.Parameters.Length, lambda.Symbol.Parameters.Length);
                i++
            )
            {
                var parameter = lambda.Symbol.Parameters[i];
                if (parameter.DeclaringSyntaxReferences.IsEmpty)
                {
                    continue;
                }

                var declaration = parameter
                    .DeclaringSyntaxReferences[0]
                    .GetSyntax(context.CancellationToken);
                var (type, annotation) = ObliviousTypes.TypeOf(compilation, invoke.Parameters[i]);
                ReportNestedLost(
                    context.ReportDiagnostic,
                    lambda.SemanticModel,
                    declaration.GetLocation(),
                    type,
                    OperationHelpers.FromSymbol(parameter),
                    invoke.Parameters[i].ContainingSymbol,
                    parameter.Name
                );
                if (parameter.NullableAnnotation == NullableAnnotation.Annotated)
                {
                    continue;
                }

                if (
                    ObliviousTypes.IsOblivious(type, annotation)
                    && lambda.SemanticModel is { } model
                    && OperationHelpers.IsNullHandledVariable(parameter, declaration, model)
                )
                {
                    continue;
                }

                ReportDelegateParameter(
                    context,
                    invoke.Parameters[i],
                    parameter.Name,
                    declaration.GetLocation()
                );
            }
        }

        public void AnalyzeMethodGroup(OperationAnalysisContext context)
        {
            var group = (IMethodReferenceOperation)context.Operation;
            if (DelegateInvoke(group) is not { } invoke)
            {
                return;
            }

            for (
                var i = 0;
                i < Math.Min(invoke.Parameters.Length, group.Method.Parameters.Length);
                i++
            )
            {
                var parameter = group.Method.Parameters[i];
                ReportNestedLost(
                    context.ReportDiagnostic,
                    group.SemanticModel,
                    group.Syntax.GetLocation(),
                    ObliviousTypes.TypeOf(compilation, invoke.Parameters[i]).Type,
                    OperationHelpers.FromSymbol(parameter),
                    invoke.Parameters[i].ContainingSymbol,
                    parameter.Name
                );
                if (parameter.NullableAnnotation != NullableAnnotation.Annotated)
                {
                    ReportDelegateParameter(
                        context,
                        invoke.Parameters[i],
                        parameter.Name,
                        group.Syntax.GetLocation()
                    );
                }
            }
        }

        private void ReportDelegateParameter(
            OperationAnalysisContext context,
            IParameterSymbol delegateParameter,
            string name,
            Location location
        )
        {
            var (type, annotation) = ObliviousTypes.TypeOf(compilation, delegateParameter);
            Report(
                context.ReportDiagnostic,
                context.Operation.SemanticModel,
                location,
                type,
                annotation,
                delegateParameter.ContainingSymbol,
                Descriptors.DelegateParameter,
                name,
                null
            );
        }

        private static IMethodSymbol? DelegateInvoke(IOperation function)
        {
            var type = function.Parent is IDelegateCreationOperation or IConversionOperation
                ? function.Parent.Type
                : null;
            // Expression<Func<...>> wraps the delegate type whose parameters are bound.
            if (type is INamedTypeSymbol { Name: "Expression", TypeArguments.Length: 1 } expression)
            {
                type = expression.TypeArguments[0];
            }

            return (type as INamedTypeSymbol)?.DelegateInvokeMethod;
        }

        // ---- NOA007: overrides and implementations ----

        public void AnalyzeType(SymbolAnalysisContext context)
        {
            var type = (INamedTypeSymbol)context.Symbol;
            foreach (var member in type.GetMembers())
            {
                // Accessors are checked through their property so an indexer parameter is reported once.
                if (
                    member is IMethodSymbol
                    {
                        OverriddenMethod: { } overridden,
                        MethodKind: not (MethodKind.PropertyGet or MethodKind.PropertySet)
                    } method
                )
                {
                    CheckParameters(context, method.Parameters, overridden.Parameters, overridden);
                }
                else if (
                    member is IPropertySymbol
                    {
                        OverriddenProperty: { } overriddenProperty
                    } property
                )
                {
                    CheckProperty(context, property, overriddenProperty);
                }
            }

            foreach (var contract in type.AllInterfaces)
            {
                foreach (var contractMember in contract.GetMembers())
                {
                    if (
                        contractMember
                            is IMethodSymbol
                            {
                                MethodKind: not (MethodKind.PropertyGet or MethodKind.PropertySet)
                            } contractMethod
                        && type.FindImplementationForInterfaceMember(contractMethod)
                            is IMethodSymbol implementation
                        && SymbolEqualityComparer.Default.Equals(
                            implementation.ContainingType,
                            type
                        )
                    )
                    {
                        CheckParameters(
                            context,
                            implementation.Parameters,
                            contractMethod.Parameters,
                            contractMethod
                        );
                    }
                    else if (
                        contractMember is IPropertySymbol contractProperty
                        && type.FindImplementationForInterfaceMember(contractProperty)
                            is IPropertySymbol implementingProperty
                        && SymbolEqualityComparer.Default.Equals(
                            implementingProperty.ContainingType,
                            type
                        )
                    )
                    {
                        CheckProperty(context, implementingProperty, contractProperty);
                    }
                }
            }
        }

        private void CheckProperty(
            SymbolAnalysisContext context,
            IPropertySymbol property,
            IPropertySymbol baseProperty
        )
        {
            CheckParameters(context, property.Parameters, baseProperty.Parameters, baseProperty);

            // Only a setter (or init) lets a value in through the property type; the override is safe if it declares it nullable.
            if (
                property.SetMethod is null
                || baseProperty.SetMethod is null
                || property.DeclaringSyntaxReferences.IsEmpty
            )
            {
                return;
            }

            var typeSyntax = property
                .DeclaringSyntaxReferences[0]
                .GetSyntax(context.CancellationToken) switch
            {
                PropertyDeclarationSyntax declaration => declaration.Type,
                IndexerDeclarationSyntax declaration => declaration.Type,
                _ => null,
            };
            if (typeSyntax is null)
            {
                return;
            }

            var (type, annotation) = ObliviousTypes.TypeOf(compilation, baseProperty);
            ReportNestedLost(
                context.ReportDiagnostic,
                null,
                typeSyntax.GetLocation(),
                type,
                (property.Type, typeSyntax),
                baseProperty,
                property.Name
            );
            if (property.NullableAnnotation == NullableAnnotation.Annotated)
            {
                return;
            }

            Report(
                context.ReportDiagnostic,
                null,
                typeSyntax.GetLocation(),
                type,
                annotation,
                baseProperty,
                Descriptors.OverrideParameter,
                property.Name,
                baseProperty.ToDisplayString(MemberFormat)
            );
        }

        private void CheckParameters(
            SymbolAnalysisContext context,
            ImmutableArray<IParameterSymbol> parameters,
            ImmutableArray<IParameterSymbol> baseParameters,
            ISymbol baseMember
        )
        {
            for (var i = 0; i < Math.Min(parameters.Length, baseParameters.Length); i++)
            {
                var parameter = parameters[i];
                if (parameter.DeclaringSyntaxReferences.IsEmpty)
                {
                    continue;
                }

                var location = parameter
                    .DeclaringSyntaxReferences[0]
                    .GetSyntax(context.CancellationToken)
                    .GetLocation();
                var (type, annotation) = ObliviousTypes.TypeOf(compilation, baseParameters[i]);
                ReportNestedLost(
                    context.ReportDiagnostic,
                    null,
                    location,
                    type,
                    OperationHelpers.FromSymbol(parameter),
                    baseMember,
                    parameter.Name
                );
                // Declaring `string?` makes the override safe even though callers of the base can pass null.
                if (parameter.NullableAnnotation == NullableAnnotation.Annotated)
                {
                    continue;
                }

                Report(
                    context.ReportDiagnostic,
                    null,
                    location,
                    type,
                    annotation,
                    baseMember,
                    Descriptors.OverrideParameter,
                    parameter.Name,
                    baseMember.ToDisplayString(MemberFormat)
                );
            }
        }

        // ---- shared reporting ----

        private void ReportValue(
            OperationAnalysisContext context,
            IOperation operation,
            ITypeSymbol? type,
            NullableAnnotation annotation,
            ISymbol? origin,
            DiagnosticDescriptor oblivious,
            string name
        )
        {
            if (
                !ObliviousTypes.IsOblivious(type, annotation)
                || OperationHelpers.IsInsideNameof(operation)
                || OperationHelpers.IsNullHandled(operation)
            )
            {
                return;
            }

            Report(
                context.ReportDiagnostic,
                operation.SemanticModel,
                operation.Syntax.GetLocation(),
                type,
                annotation,
                origin,
                oblivious,
                name,
                null
            );
        }

        /// <param name="model">The model of the analysis in progress; null in symbol actions, which obtain one only once a report is certain.</param>
        private void Report(
            Action<Diagnostic> report,
            SemanticModel? model,
            Location location,
            ITypeSymbol? type,
            NullableAnnotation annotation,
            ISymbol? origin,
            DiagnosticDescriptor oblivious,
            string name,
            string? extra
        )
        {
            if (
                !ObliviousTypes.IsOblivious(type, annotation)
                || !IsReported(model, location, origin)
            )
            {
                return;
            }

            report(
                Diagnostic.Create(
                    oblivious,
                    location,
                    name,
                    type!.ToDisplayString(TypeFormat),
                    extra
                )
            );
        }

        /// <summary>
        /// Reports NOA009 when a nested oblivious position of the source type is declared non-null by the target type.
        /// The target's type syntax, when there is one, goes along as an additional location with the paths of the
        /// lost positions, so a code fix can declare them nullable.
        /// </summary>
        private void ReportNestedLost(
            Action<Diagnostic> report,
            SemanticModel? model,
            Location location,
            ITypeSymbol? source,
            (ITypeSymbol Type, TypeSyntax? Syntax)? target,
            ISymbol? origin,
            string name
        )
        {
            if (source is null || target is not var (targetType, targetSyntax))
            {
                return;
            }

            var paths = NestedPositions.LostPaths(source, targetType);
            if (paths.Count == 0 || !IsReported(model, location, origin))
            {
                return;
            }

            report(
                Diagnostic.Create(
                    Descriptors.NestedObliviousLost,
                    location,
                    targetSyntax is null ? null : [targetSyntax.GetLocation()],
                    targetSyntax is null
                        ? null
                        : ImmutableDictionary<string, string?>.Empty.Add(
                            NestedPositions.LostPathsProperty,
                            string.Join(";", paths.Select(p => p.Path))
                        ),
                    name,
                    string.Join(
                        "', '",
                        paths.Select(p => p.Type.ToDisplayString(TypeFormat)).Distinct()
                    ),
                    targetType.ToDisplayString(AnnotatedTypeFormat)
                )
            );
        }

        /// <summary>Reports only in nullable-enabled code and for members that are not excluded.</summary>
        private bool IsReported(SemanticModel? model, Location location, ISymbol? origin) =>
            location.SourceTree is { } tree
            && (model ?? compilation.GetSemanticModel(tree))
                .GetNullableContext(location.SourceSpan.Start)
                .HasFlag(NullableContext.AnnotationsEnabled)
            && !settings.IsExcluded(origin, tree);

        private static bool IsQueryClause(SyntaxNode syntax) =>
            syntax
                is QueryClauseSyntax
                    or SelectOrGroupClauseSyntax
                    or OrderingSyntax
                    or QueryBodySyntax
                    or QueryExpressionSyntax
                    or QueryContinuationSyntax;

        private static string Describe(IOperation operation)
        {
            var text = operation.Syntax.ToString();
            return text.Length <= 40 ? text : text.Substring(0, 40) + "...";
        }
    }
}
