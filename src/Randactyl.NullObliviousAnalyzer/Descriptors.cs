using Microsoft.CodeAnalysis;

namespace Randactyl.NullObliviousAnalyzer;

internal static class Descriptors
{
    private const string Category = "Nullability";

    public static readonly DiagnosticDescriptor CallResult = Create(
        "NOA001", "Call returns a null-oblivious reference type",
        "'{0}' returns the null-oblivious type '{1}'");

    public static readonly DiagnosticDescriptor PropertyRead = Create(
        "NOA002", "Property or indexer read yields a null-oblivious reference type",
        "Reading '{0}' yields the null-oblivious type '{1}'");

    public static readonly DiagnosticDescriptor FieldRead = Create(
        "NOA003", "Field read yields a null-oblivious reference type",
        "Reading field '{0}' yields the null-oblivious type '{1}'");

    public static readonly DiagnosticDescriptor RefOrOutArgument = Create(
        "NOA004", "out/ref argument may receive a null-oblivious reference type",
        "The callee may assign a null-oblivious value of type '{1}' through parameter '{0}'");

    public static readonly DiagnosticDescriptor CollectionElement = Create(
        "NOA005", "Element read from a null-oblivious collection",
        "'{0}' receives an element of the null-oblivious type '{1}'");

    public static readonly DiagnosticDescriptor DelegateParameter = Create(
        "NOA006", "Delegate parameter bound to a lambda or method group is null-oblivious",
        "Parameter '{0}' is bound to a delegate parameter of the null-oblivious type '{1}'");

    public static readonly DiagnosticDescriptor OverrideParameter = Create(
        "NOA007", "Parameter overrides or implements a null-oblivious parameter",
        "Parameter '{0}' overrides or implements '{2}' whose parameter has the null-oblivious type '{1}'");

    public static readonly DiagnosticDescriptor NestedOblivious = Create(
        "NOA008", "Null-oblivious reference type nested in a type argument, array element or tuple element",
        "'{0}' has type '{1}' which contains a null-oblivious reference type",
        DiagnosticSeverity.Info);

    private static DiagnosticDescriptor Create(string id, string title, string message,
        DiagnosticSeverity severity = DiagnosticSeverity.Warning) =>
        new(id, title, message, Category, severity, isEnabledByDefault: true);
}
