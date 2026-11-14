using System;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;

namespace Randactyl.NullObliviousAnalyzer.Testing;

/// <summary>
/// Applies one code fix (chosen by its equivalence key) to every diagnostic it fixes and compares the result.
/// Markup in <c>fixedSource</c> lists the diagnostics that remain after the fix.
/// </summary>
public static class CodeFixVerifier<TCodeFix>
    where TCodeFix : CodeFixProvider, new()
{
    public static Task VerifyAsync(
        string source,
        string fixedSource,
        string equivalenceKey,
        string obliviousLibrarySource
    ) => VerifyAsync(source, fixedSource, equivalenceKey, o => o.ObliviousLibrarySource = obliviousLibrarySource);

    public static Task VerifyAsync(
        string source,
        string fixedSource,
        string equivalenceKey,
        Action<AnalyzerTestOptions> configure
    )
    {
        var options = new AnalyzerTestOptions();
        configure(options);
        // The framework applies the fix one diagnostic at a time and through Fix All, and expects both to agree.
        var test = new CSharpCodeFixTest<NullObliviousAnalyzer, TCodeFix, DefaultVerifier>
        {
            CodeActionEquivalenceKey = equivalenceKey,
        };
        AnalyzerVerifier.Configure(test, source, options);
        test.FixedState.Sources.Add((options.FileName, SourceText.From(fixedSource)));
        // Markup in the fixed source lists reports this fix is not offered for, even if their ID is fixable.
        test.FixedState.MarkupHandling = MarkupMode.Allow;
        return test.RunAsync();
    }
}
