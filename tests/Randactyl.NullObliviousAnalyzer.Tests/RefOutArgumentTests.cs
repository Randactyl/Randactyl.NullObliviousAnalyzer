using System.Threading.Tasks;
using Randactyl.NullObliviousAnalyzer.Testing;

namespace Randactyl.NullObliviousAnalyzer.Tests;

/// <summary>NOA004.</summary>
public class RefOutArgumentTests
{
    [Test]
    public Task Out_and_ref_arguments_from_oblivious_assembly()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    Foo f;
                    lib.Fill({|NOA004:out f|});
                    lib.Fill({|NOA004:out var g|});
                    lib.Swap({|NOA004:ref f|});
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Out_argument_from_same_file_disabled_region()
    {
        const string source = """
            #nullable disable
            public class Local { public void Get(out string s) { s = null; } }
            #nullable enable
            class C { void M(Local l) { l.Get({|NOA004:out var s|}); } }
            """;
        return AnalyzerVerifier.VerifyAsync(source);
    }

    [Test]
    public Task Value_type_and_annotated_out_parameters_are_not_reported()
    {
        const string source = """
            class Annotated { public void Get(out string s) { s = ""; } public void Maybe(out string? s) { s = null; } }
            class C
            {
                void M(Lib lib, Annotated a)
                {
                    lib.FillInt(out int i);
                    a.Get(out string s);
                    a.Maybe(out string? t);
                    int.TryParse("1", out int n);
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task By_value_arguments_are_not_reported()
    {
        const string source = """
            #nullable disable
            public class Local { public void Take(string s) { } }
            #nullable enable
            class C { void M(Local l) { l.Take("x"); } }
            """;
        return AnalyzerVerifier.VerifyAsync(source);
    }
}
