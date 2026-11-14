; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
NOA009 | Nullability | Warning | Null-oblivious type argument, array element or tuple element is declared non-null

### Removed Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
NOA008 | Nullability | Info | Null-oblivious reference type nested in a type argument, array element or tuple element
