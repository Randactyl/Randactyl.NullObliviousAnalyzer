using System.Threading.Tasks;
using Randactyl.NullObliviousAnalyzer.Testing;
using Xunit;

namespace Randactyl.NullObliviousAnalyzer.Tests;

/// <summary>NOA005.</summary>
public class CollectionElementTests
{
    [Fact]
    public Task Array_element_read_from_oblivious_assembly()
    {
        const string source = """
            class C { void M(Lib lib) { var a = {|NOA005:{|NOA003:lib.Array|}[0]|}; } }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Array_element_read_from_same_file_disabled_region()
    {
        const string source = """
            #nullable disable
            public class Local { public string[] Texts => null; }
            #nullable enable
            class C { void M(Local l) { var a = {|NOA005:{|NOA002:l.Texts|}[0]|}; } }
            """;
        return AnalyzerVerifier.VerifyAsync(source);
    }

    [Fact]
    public Task Array_element_write_is_not_a_read()
    {
        const string source = """
            class C { void M(Lib lib, Foo f) { {|NOA003:lib.Array|}[0] = f; } }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Annotated_and_value_type_arrays_report_only_the_oblivious_Ints_field()
    {
        const string source = """
            class C
            {
                void M(string[] strings, string?[] maybe, Foo[] foos, Lib lib)
                {
                    var a = strings[0];
                    var b = maybe[0];
                    var c = foos[0];
                    var d = {|NOA003:lib.Ints|}[0];
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Foreach_variable_over_oblivious_array_and_list()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    foreach (Foo {|NOA005:a|} in {|NOA003:lib.Array|}) { }
                    foreach (var {|NOA005:b|} in {|NOA003:lib.Items|}) { }
                    foreach (var {|NOA005:c|} in {|NOA003:lib.Names|}) { }
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Foreach_over_annotated_collections_reports_only_the_oblivious_Ints_field()
    {
        const string source = """
            using System.Collections.Generic;
            class C
            {
                void M(List<string> list, string[] array, Lib lib)
                {
                    foreach (var a in list) { }
                    foreach (var b in array) { }
                    foreach (var c in {|NOA003:lib.Ints|}) { }
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Foreach_inside_nullable_disable_is_not_reported()
    {
        const string source = """
            #nullable disable
            class C { void M(Lib lib) { foreach (var a in lib.Array) { } } }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }
}
