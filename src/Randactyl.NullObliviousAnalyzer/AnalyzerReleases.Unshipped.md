; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
NOA001 | Nullability | Warning | Call returns a null-oblivious reference type
NOA002 | Nullability | Warning | Property or indexer read yields a null-oblivious reference type
NOA003 | Nullability | Warning | Field read yields a null-oblivious reference type
NOA004 | Nullability | Warning | out/ref argument may receive a null-oblivious reference type
NOA005 | Nullability | Warning | Element read, iterated or spread from a null-oblivious collection
NOA006 | Nullability | Warning | Delegate parameter bound to a lambda or method group is null-oblivious
NOA007 | Nullability | Warning | Parameter or property value overrides or implements a null-oblivious one
NOA008 | Nullability | Info | Null-oblivious reference type nested in a type argument, array element or tuple element
