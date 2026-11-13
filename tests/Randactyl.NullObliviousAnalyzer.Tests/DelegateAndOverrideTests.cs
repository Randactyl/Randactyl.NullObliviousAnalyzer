using System.Threading.Tasks;
using Randactyl.NullObliviousAnalyzer.Testing;

namespace Randactyl.NullObliviousAnalyzer.Tests;

/// <summary>NOA006 (lambda and method group parameters) and NOA007 (override and implementation parameters).</summary>
public class DelegateAndOverrideTests
{
    [Test]
    public Task Lambda_parameters_bound_to_oblivious_delegate()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    Handler h = ({|NOA006:s|}, {|NOA006:e|}) => { };
                    lib.Changed += ({|NOA006:s|}, {|NOA006:e|}) => { };
                    lib.Each({|NOA006:f|} => { });
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Anonymous_method_parameters()
    {
        const string source = """
            class C { void M() { FooAction a = delegate ({|NOA006:Foo f|}) { }; } }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Lambda_for_delegate_in_same_file_disabled_region()
    {
        const string source = """
            #nullable disable
            public delegate void Callback(string text);
            #nullable enable
            class C { void M() { Callback c = {|NOA006:t|} => { }; } }
            """;
        return AnalyzerVerifier.VerifyAsync(source);
    }

    [Test]
    public Task Method_group_is_reported_on_the_method_group_expression()
    {
        const string source = """
            class C
            {
                void OnFoo(Foo f) { }
                void M(Lib lib) { lib.SubscribeFoo({|NOA006:OnFoo|}); }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Lambda_with_explicitly_nullable_parameter_or_annotated_delegate_is_not_reported()
    {
        const string source = """
            using System;
            class C
            {
                void M()
                {
                    Handler h = (object? s, EventArgs? e) => { };
                    Action<Foo> a = f => { };
                    Action<int> b = i => { };
                    Func<string, string> c = s => s;
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Override_parameter_of_oblivious_base_member()
    {
        const string source = """
            class Impl : BaseWithParams
            {
                public override void Take({|NOA007:Foo f|}, int n) { }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Implicit_and_explicit_interface_implementation()
    {
        const string source = """
            class Implicit : IAccepts { public void Accept({|NOA007:Foo f|}) { } }
            class Explicit : IAccepts { void IAccepts.Accept({|NOA007:Foo f|}) { } }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Override_in_same_file_disabled_region()
    {
        const string source = """
            #nullable disable
            public class Base { public virtual void Take(string s) { } }
            #nullable enable
            class Derived : Base { public override void Take({|NOA007:string s|}) { } }
            """;
        return AnalyzerVerifier.VerifyAsync(source);
    }

    [Test]
    public Task Nullable_parameter_and_annotated_members_are_not_reported()
    {
        const string source = """
            class Impl : BaseWithParams, IAccepts, IAcceptsGeneric<string>
            {
                public override void Take(Foo? f, int n) { }
                public void Accept(Foo? f) { }
                public void Accept(string? s) { }
                public override string ToString() => "";
                public override bool Equals(object? obj) => false;
                public override int GetHashCode() => 0;
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }
}
