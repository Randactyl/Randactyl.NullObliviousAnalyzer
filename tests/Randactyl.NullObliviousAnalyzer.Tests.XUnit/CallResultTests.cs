using System.Threading.Tasks;
using Randactyl.NullObliviousAnalyzer.Testing;
using Xunit;

namespace Randactyl.NullObliviousAnalyzer.Tests;

public class CallResultTests
{
    [Fact]
    public Task Method_from_oblivious_assembly()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    var a = {|NOA001:lib.GetFoo()|};
                    var b = {|NOA001:Lib.StaticFoo()|};
                    var s = {|NOA001:lib.GetString()|};
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Method_from_same_file_nullable_disabled_region()
    {
        const string source = """
            #nullable disable
            public class Local { public string Name() => null; public static Local Make() => null; }
            #nullable enable
            class C
            {
                void M(Local l)
                {
                    var s = {|NOA001:l.Name()|};
                    var m = {|NOA001:Local.Make()|};
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source);
    }

    [Fact]
    public Task Extension_method_in_disabled_region()
    {
        const string source = """
            #nullable disable
            public static class Ext { public static string Get(this int i) => null; }
            #nullable enable
            class C { void M() { var s = {|NOA001:5.Get()|}; } }
            """;
        return AnalyzerVerifier.VerifyAsync(source);
    }

    [Fact]
    public Task Delegate_invocation()
    {
        const string source = """
            class C { void M(FooFactory f) { var x = {|NOA001:f()|}; } }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Local_function_declared_in_disabled_region()
    {
        const string source = """
            class C
            {
                void M()
                {
            #nullable disable
                    string Local() => null;
            #nullable enable
                    var x = {|NOA001:Local()|};
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source);
    }

    [Fact]
    public Task User_defined_operator_and_conversion()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    var sum = {|NOA001:lib + lib|};
                    Foo converted = {|NOA001:lib|};
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Await_with_oblivious_result_reports_the_await_and_a_directly_awaited_oblivious_task()
    {
        const string source = """
            using System.Threading.Tasks;
            class C
            {
                async Task M(Lib lib)
                {
                    Foo a = {|NOA001:await {|NOA001:lib.GetFooAsync()|}|};
                    Foo b = {|NOA001:await lib.GetFooValueAsync()|};
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Await_of_annotated_task_reports_only_the_oblivious_GetIntAsync_call()
    {
        const string source = """
            using System.Threading.Tasks;
            class C
            {
                async Task M(Task<Foo> t, Task<int> i, Lib lib)
                {
                    Foo a = await t;
                    int b = await i;
                    int c = await {|NOA001:lib.GetIntAsync()|};
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Annotated_value_and_nullable_results_are_not_reported()
    {
        const string source = """
            class Annotated { public string Get() => ""; public string? GetOrNull() => null; public static T Id<T>(T t) => t; }
            class C
            {
                void M(Lib lib, Annotated a)
                {
                    var i = lib.GetInt();
                    var n = lib.GetNullableInt();
                    var v = lib.GetVal();
                    var s = a.Get();
                    var o = a.GetOrNull();
                    var g = Annotated.Id("text");
                    var k = Annotated.Id(5);
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Generic_method_substituted_with_oblivious_type_argument()
    {
        const string source = """
            using System.Linq;
            class C
            {
                void M(Lib lib)
                {
                    var first = {|NOA001:{|NOA003:lib.Items|}.First()|};
                    var name = {|NOA001:{|NOA003:lib.Names|}.First()|};
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Use_site_inside_nullable_disable_is_not_reported()
    {
        const string source = """
            #nullable disable
            class C { void M(Lib lib) { var a = lib.GetFoo(); } }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Use_site_with_annotations_disabled_is_not_reported()
    {
        const string source = """
            #nullable disable
            #nullable enable warnings
            class C { void M(Lib lib) { var a = lib.GetFoo(); } }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Generated_code_is_skipped_by_header()
    {
        const string source = """
            // <auto-generated/>
            class C { void M(Lib lib) { var a = lib.GetFoo(); } }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Generated_code_is_skipped_by_file_name()
    {
        const string source = """
            class C { void M(Lib lib) { var a = lib.GetFoo(); } }
            """;
        return AnalyzerVerifier.VerifyAsync(source, o =>
        {
            o.ObliviousLibrarySource = Libs.Oblivious;
            o.FileName = "/0/Thing.g.cs";
        });
    }

    [Fact]
    public Task Nameof_operand_is_never_reported()
    {
        const string source = """
            class C { void M(Lib lib) { var a = nameof(lib.GetFoo); var b = nameof(Lib.StaticFoo); } }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }
}
