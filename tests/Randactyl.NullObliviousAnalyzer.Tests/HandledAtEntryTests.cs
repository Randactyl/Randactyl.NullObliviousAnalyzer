using System.Threading.Tasks;
using Randactyl.NullObliviousAnalyzer.Testing;

namespace Randactyl.NullObliviousAnalyzer.Tests;

/// <summary>
/// A value is handled where it arrives when the code that consumes it deals with null, or hands it to a
/// declaration the compiler's own nullable analysis treats as maybe-null. Handled values are not reported.
/// </summary>
public class HandledAtEntryTests
{
    [Test]
    public Task Conditional_access_receiver_is_handled()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    var a = lib.GetFoo()?.Count;
                    var b = lib.Array?[0];
                    var c = (lib.Prop)?.Count;
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Value_produced_by_a_conditional_access_is_handled_but_values_dereferenced_inside_it_are_not()
    {
        // The result of ?. is maybe-null to the compiler; Wrapper.Inner in the middle is dereferenced with a plain dot.
        const string source = """
            class C
            {
                void M(Lib lib, Wrapper w, Wrapper? maybe)
                {
                    var a = maybe?.Label;
                    var b = maybe?.Inner?.Name;
                    var c = maybe?{|NOA003:.Inner|}.Name;
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Coalesce_and_coalesce_assignment_are_handled()
    {
        const string source = """
            using System;
            class C
            {
                void M(Lib lib, Foo fallback)
                {
                    Foo a = lib.GetFoo() ?? fallback;
                    Foo b = lib.Prop ?? throw new InvalidOperationException();
                    lib.Prop ??= fallback;
                    lib.Field ??= fallback;
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Patterns_and_null_comparisons_are_handled_except_var_patterns()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    if (lib.GetFoo() is { } a) { }
                    if (lib.GetFoo() is not null) { }
                    if (lib.GetFoo() is null) { }
                    if (lib.Prop is Foo b) { }
                    if (lib.Prop == null) { }
                    if (null != lib.Field) { }
                    if ({|NOA001:lib.GetFoo()|} is var c) { }
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Argument_to_a_nullable_parameter_is_handled()
    {
        const string source = """
            class C
            {
                void Maybe(Foo? f) { }
                void Sure(Foo f) { }

                void M(Lib lib)
                {
                    Maybe(lib.GetFoo());
                    System.Console.WriteLine(lib.GetString());
                    var empty = string.IsNullOrEmpty(lib.GetString());
                    Sure({|NOA001:lib.GetFoo()|});
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Return_from_a_nullable_member_is_handled()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            class C
            {
                Foo? A(Lib lib) => lib.GetFoo();
                Foo? B(Lib lib) { return lib.Prop; }
                Foo? P(Lib lib) => lib.Field;
                Foo? Prop => new Lib().GetFoo();
                async Task<Foo?> D(Lib lib) { await Task.Yield(); return lib.GetFoo(); }
                IEnumerable<Foo?> E(Lib lib) { yield return lib.GetFoo(); }

                void M(Lib lib)
                {
                    Func<Foo?> f = () => lib.GetFoo();
                    Foo? Local() => lib.GetFoo();
                }

                Foo Sure(Lib lib) => {|NOA001:lib.GetFoo()|};
                async Task<Foo> SureAsync(Lib lib) { await Task.Yield(); return {|NOA001:lib.GetFoo()|}; }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Cast_to_nullable_is_handled()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    var a = (Foo?)lib.GetFoo();
                    var b = (Foo?)(lib.Prop);
                    var c = (Foo){|NOA001:lib.GetFoo()|};
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Null_forgiving_operator_is_an_explicit_decision()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    var a = lib.GetFoo()!;
                    var b = lib.Prop!.Count;
                    var c = lib.Field!;
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Storing_in_a_nullable_variable_is_not_handled()
    {
        // The compiler gives an oblivious value a not-null state, and a nullable local or field takes that state over,
        // so a later dereference would not warn.
        const string source = """
            class C
            {
                Foo? field;

                void M(Lib lib)
                {
                    Foo? a = {|NOA001:lib.GetFoo()|};
                    Foo? b;
                    b = {|NOA002:lib.Prop|};
                    field = {|NOA003:lib.Field|};
                    var c = {|NOA001:lib.GetFoo()|};
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Dereference_is_not_handled()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    var a = {|NOA001:lib.GetFoo()|}.Count;
                    var b = {|NOA002:lib.Prop|}.Name?.Length;
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }
}
