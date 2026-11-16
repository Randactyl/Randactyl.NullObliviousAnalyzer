using System.Threading.Tasks;
using Randactyl.NullObliviousAnalyzer.Testing;

namespace Randactyl.NullObliviousAnalyzer.Tests;

/// <summary>
/// NOA009: an oblivious type argument, array element or tuple element reaches a type that declares it non-null.
/// A target that keeps it oblivious (var) is not reported; the element is reported when it comes out.
/// A target that declares it nullable (Foo?[], List&lt;Foo?&gt;) handles it.
/// </summary>
public class NestedObliviousTests
{
    [Test]
    public Task Locals_fields_and_properties()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            class C
            {
                List<Foo> field = new();
                List<Foo> Prop { get; set; } = new();
                static Lib shared = new();
                List<Foo> initialized = {|NOA009:{|NOA003:shared.Items|}.ToList()|};

                void M(Lib lib)
                {
                    Foo[] a = {|NOA009:{|NOA003:lib.Items|}.ToArray()|};
                    Foo?[] b = {|NOA003:lib.Items|}.ToArray();
                    var c = {|NOA003:lib.Items|}.ToArray();
                    Foo[] d = {|NOA009:c|};
                    field = {|NOA009:{|NOA003:lib.Items|}.ToList()|};
                    Prop = {|NOA009:{|NOA003:lib.Items|}.ToList()|};
                    (Foo, int) e = {|NOA009:lib.GetPair()|};
                    (Foo?, int) f = lib.GetPair();
                    var g = lib.GetPair();
                    ValueTask<Foo> h = {|NOA009:lib.GetFooValueAsync()|};
                    var i = lib.GetFooValueAsync();
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Conversions_arguments_and_returns()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            class C
            {
                void Take(IEnumerable<Foo> items) { }
                void TakeMaybe(IEnumerable<Foo?> items) { }
                IEnumerable<Foo> Ret(Lib lib) => {|NOA009:{|NOA003:lib.Items|}.ToList()|};

                void M(Lib lib)
                {
                    var list = {|NOA003:lib.Items|}.ToList();
                    IEnumerable<Foo> a = {|NOA009:list|};
                    IEnumerable<Foo?> b = list;
                    IEnumerable<object> o = {|NOA009:list|};
                    Take({|NOA009:list|});
                    TakeMaybe(list);
                    var c = new List<Foo>({|NOA009:list|});
                    Func<List<Foo>> f = () => {|NOA009:list|};
                    var d = (List<Foo?>)list;
                    var e = list.OfType<Foo>().ToList();
                    var w = list.Where(x => x is not null);
                    List<Foo> g = {|NOA009:list.Where(x => x is not null).ToList()|};
                    List<Foo> h = list!;
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Async_iterator_await_tuple_and_collection_targets()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            class C
            {
                async Task<List<Foo>> A(Lib lib)
                {
                    await Task.Yield();
                    var x = {|NOA003:lib.Items|}.ToList();
                    return {|NOA009:x|};
                }

                IEnumerable<Foo[]> B(Lib lib)
                {
                    yield return {|NOA009:{|NOA003:lib.Items|}.ToArray()|};
                }

                async Task M(Lib lib)
                {
                    var list = {|NOA003:lib.Items|}.ToList();
                    List<Foo> y = {|NOA009:await Task.FromResult(list)|};
                    var arr = list.ToArray();
                    (Foo[], int) t = ({|NOA009:arr|}, 1);
                    (Foo?[], int) u = (arr, 1);
                    List<Foo[]> c = [{|NOA009:arr|}];
                    List<Foo?[]> d = [arr];
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Conditional_coalesce_and_patterns()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            class C
            {
                void M(Lib lib, bool flag)
                {
                    var list = {|NOA003:lib.Items|}.ToList();
                    List<Foo> a = flag ? {|NOA009:list|} : new List<Foo>();
                    List<Foo> b = {|NOA009:lib.Items|} ?? new List<Foo>();
                    foreach (var {|NOA005:e|} in lib.Items ?? new List<Foo>()) { e.ToString(); }
                    if ({|NOA009:list|} is List<Foo> l) { }
                    object boxed = list;
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Out_arguments_deconstruction_foreach_lambdas_and_overrides()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            class C
            {
                void M(Lib lib)
                {
                    lib.FillPair(out var p);
                    lib.FillPair({|NOA009:out (Foo, int) q|});
                    var arr = {|NOA003:lib.Items|}.ToArray();
                    (Foo[] {|NOA009:x|}, int n) = (arr, 1);
                    var (z, k) = (arr, 1);
                    foreach (Foo[] {|NOA009:group|} in {|NOA003:lib.Groups|}) { }
                    foreach (var group2 in {|NOA003:lib.Groups|}) { }
                    lib.EachList(({|NOA009:List<Foo> items|}) => { });
                    lib.EachList((List<Foo?> items) => { });
                }
            }

            class Derived : BaseWithParams
            {
                public override void TakeList({|NOA007:{|NOA009:List<Foo> items|}|}) { }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Fully_annotated_nested_types_are_not_reported()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            class C
            {
                void M(List<string> list, (string, int) pair)
                {
                    string[] a = list.ToArray();
                    List<string> b = list.ToList();
                    (string, int) c = pair;
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source);
    }
}
