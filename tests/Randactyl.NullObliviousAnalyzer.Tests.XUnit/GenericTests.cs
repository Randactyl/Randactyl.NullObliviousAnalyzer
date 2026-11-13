using System.Threading.Tasks;
using Randactyl.NullObliviousAnalyzer.Testing;
using Xunit;

namespace Randactyl.NullObliviousAnalyzer.Tests;

/// <summary>
/// Type parameters declared without annotations, used with an annotated type argument. Roslyn shows the
/// substituted member as a plain non-null type, so the analyzer looks at the original definition.
/// </summary>
public class GenericTests
{
    [Fact]
    public Task Generic_method_result_with_annotated_type_argument()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    var a = {|NOA001:lib.Resolve<Foo>()|};
                    var b = {|NOA001:lib.Resolve<string>()|};
                    var c = {|NOA001:lib.ResolveClass<Foo>()|};
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Generic_type_members_with_annotated_type_argument()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    var box = new Box<Foo>();
                    var a = {|NOA003:box.Value|};
                    var b = {|NOA002:box.Prop|};
                    var c = {|NOA002:box[0]|};
                    var d = {|NOA001:box.Get()|};
                    box.TryGet({|NOA004:out var v|});
                    box.Value = new Foo();
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Generic_out_argument_with_annotated_type_argument()
    {
        const string source = """
            class C { void M(Lib lib) { lib.TryResolve<Foo>({|NOA004:out var f|}); } }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Type_argument_that_is_a_type_parameter_of_the_caller()
    {
        const string source = """
            class C
            {
                T Unconstrained<T>(Lib lib) => {|NOA001:lib.Resolve<T>()|};
                T Reference<T>(Lib lib) where T : class => {|NOA001:lib.Resolve<T>()|};
                T NotNull<T>(Lib lib) where T : notnull => {|NOA001:lib.Resolve<T>()|};
                T Value<T>(Lib lib) where T : struct => lib.Resolve<T>();
                T? Nullable<T>(Lib lib) where T : class => lib.Resolve<T?>();
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Value_type_and_explicitly_nullable_type_arguments_are_not_reported()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    var a = lib.Resolve<int>();
                    var b = lib.Resolve<Val>();
                    var c = lib.Resolve<Foo?>();
                    var d = lib.Resolve<int?>();
                    var box = new Box<Foo?>();
                    var e = box.Value;
                    var f = box.Get();
                    box.TryGet(out var g);
                    var h = new Box<int>().Value;
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Annotated_generics_from_the_base_class_library_are_not_reported()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            class C
            {
                async Task M(List<string> list, Dictionary<string, string> map, Task<string> task, IEnumerable<string> sequence)
                {
                    var a = list[0];
                    var b = map.TryGetValue("k", out var v);
                    var c = await task;
                    var d = list.First();
                    var e = sequence.FirstOrDefault();
                    foreach (var s in list) { }
                    foreach (var t in sequence) { }
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source);
    }

    [Fact]
    public Task Substituted_type_argument_in_awaited_results()
    {
        const string source = """
            using System.Threading.Tasks;
            class C
            {
                async Task M(Lib lib)
                {
                    var a = {|NOA001:await {|NOA001:lib.ResolveAsync<Foo>()|}|};
                    var b = {|NOA001:await lib.ResolveValueAsync<Foo>()|};
                    var c = await lib.ResolveValueAsync<int>();
                    var d = await lib.ResolveValueAsync<Foo?>();
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Substituted_type_argument_nested_in_a_value_type_is_NOA008()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    var a = {|NOA008:lib.ResolvePair<Foo>()|};
                    var b = lib.ResolvePair<int>();
                    var c = lib.ResolvePair<Foo?>();
                    var d = {|NOA008:lib.ResolveValueAsync<Foo>()|};
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Substituted_array_element_and_enumeration()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    var a = {|NOA005:{|NOA001:lib.ResolveArray<Foo>()|}[0]|};
                    foreach (var {|NOA005:x|} in {|NOA001:lib.ResolveArray<Foo>()|}) { }
                    foreach (var {|NOA005:y|} in {|NOA001:lib.Stream<Foo>()|}) { }
                    foreach (var z in {|NOA001:lib.Stream<int>()|}) { }
                    foreach (var w in {|NOA001:lib.Stream<Foo?>()|}) { }
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Lambda_and_method_group_bound_to_oblivious_generic_delegate()
    {
        const string source = """
            class C
            {
                void OnFoo(Foo f) { }
                void M(Lib lib)
                {
                    lib.Listen<Foo>({|NOA006:f|} => { });
                    Handler<Foo> h = {|NOA006:f|} => { };
                    lib.Listen<Foo>({|NOA006:OnFoo|});
                    Handler<Foo?> n = f => { };
                    Handler<int> i = x => { };
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Override_and_implementation_of_oblivious_generic_parameter()
    {
        const string source = """
            class Impl : IAcceptsGeneric<Foo> { public void Accept({|NOA007:Foo f|}) { } }
            class Explicit : IAcceptsGeneric<string> { void IAcceptsGeneric<string>.Accept({|NOA007:string s|}) { } }
            class Nullable : IAcceptsGeneric<Foo?> { public void Accept(Foo? f) { } }
            class Value : IAcceptsGeneric<int> { public void Accept(int i) { } }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Derived_generic_class_passes_its_type_parameter()
    {
        const string source = """
            class Mine<U> : Box<U>
            {
                void M() { var a = {|NOA003:Value|}; var b = {|NOA001:Get()|}; }
            }
            class MyStruct<U> : Box<U> where U : struct
            {
                void M() { var a = Value; var b = Get(); }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }
}
