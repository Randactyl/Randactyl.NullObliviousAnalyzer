using System.Threading.Tasks;
using Randactyl.NullObliviousAnalyzer.CodeFixes;
using Randactyl.NullObliviousAnalyzer.Testing;

namespace Randactyl.NullObliviousAnalyzer.Tests;

/// <summary>Each fix must make its report go away, and only the reports it does not handle may remain.</summary>
public class CodeFixTests
{
    [Test]
    public Task Throw_if_null_adds_coalesce_throw_and_parenthesizes_when_needed()
    {
        const string source = """
            using System;
            class C
            {
                void M(Lib lib)
                {
                    var a = {|NOA001:lib.GetFoo()|};
                    var c = {|NOA001:lib.GetFoo()|}.Count;
                }
            }
            """;
        const string fixedSource = """
            using System;
            class C
            {
                void M(Lib lib)
                {
                    var a = lib.GetFoo() ?? throw new InvalidOperationException("lib.GetFoo() is null.");
                    var c = (lib.GetFoo() ?? throw new InvalidOperationException("lib.GetFoo() is null.")).Count;
                }
            }
            """;
        return CodeFixVerifier<ExpressionCodeFixProvider>.VerifyAsync(
            source,
            fixedSource,
            ExpressionCodeFixProvider.ThrowKey,
            Libs.Oblivious
        );
    }

    [Test]
    public Task Conditional_access_is_offered_where_the_result_can_become_null()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    {|NOA001:lib.GetFoo()|}.ToString();
                    var name = {|NOA003:{|NOA001:lib.GetFoo()|}.Name|};
                    var count = {|NOA001:lib.GetFoo()|}.Count;
                }
            }
            """;
        const string fixedSource = """
            class C
            {
                void M(Lib lib)
                {
                    lib.GetFoo()?.ToString();
                    var name = lib.GetFoo()?.Name;
                    var count = {|NOA001:lib.GetFoo()|}.Count;
                }
            }
            """;
        return CodeFixVerifier<ExpressionCodeFixProvider>.VerifyAsync(
            source,
            fixedSource,
            ExpressionCodeFixProvider.ConditionalAccessKey,
            Libs.Oblivious
        );
    }

    [Test]
    public Task Return_early_turns_a_local_declaration_into_a_guard()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    Foo a = {|NOA001:lib.GetFoo()|};
                    var b = {|NOA002:lib.Prop|};
                    a.ToString();
                    b.ToString();
                }

                int N(Lib lib)
                {
                    var c = {|NOA001:lib.GetFoo()|};
                    return c.Count;
                }
            }
            """;
        const string fixedSource = """
            class C
            {
                void M(Lib lib)
                {
                    if (lib.GetFoo() is not Foo a) return;
                    if (lib.Prop is not { } b) return;
                    a.ToString();
                    b.ToString();
                }

                int N(Lib lib)
                {
                    var c = {|NOA001:lib.GetFoo()|};
                    return c.Count;
                }
            }
            """;
        return CodeFixVerifier<ExpressionCodeFixProvider>.VerifyAsync(
            source,
            fixedSource,
            ExpressionCodeFixProvider.ReturnKey,
            Libs.Oblivious
        );
    }

    [Test]
    public Task Assert_adds_the_null_forgiving_operator()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    var a = {|NOA001:lib.GetFoo()|};
                    var b = {|NOA003:lib.Field|}.Count;
                }
            }
            """;
        const string fixedSource = """
            class C
            {
                void M(Lib lib)
                {
                    var a = lib.GetFoo()!;
                    var b = lib.Field!.Count;
                }
            }
            """;
        return CodeFixVerifier<ExpressionCodeFixProvider>.VerifyAsync(
            source,
            fixedSource,
            ExpressionCodeFixProvider.AssertKey,
            Libs.Oblivious
        );
    }

    [Test]
    public Task Out_variable_is_renamed_and_guarded()
    {
        const string source = """
            using System;
            class C
            {
                void M(Lib lib)
                {
                    lib.Fill({|NOA004:out var d|});
                    d.ToString();
                }
            }
            """;
        const string fixedSource = """
            using System;
            class C
            {
                void M(Lib lib)
                {
                    lib.Fill(out var dOrNull);
                    if (dOrNull is not { } d) throw new InvalidOperationException("d is null.");
                    d.ToString();
                }
            }
            """;
        return CodeFixVerifier<VariableCodeFixProvider>.VerifyAsync(
            source,
            fixedSource,
            VariableCodeFixProvider.ThrowKey,
            Libs.Oblivious
        );
    }

    [Test]
    public Task Deconstruction_target_is_renamed_and_guarded()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    var ({|NOA004:a|}, b) = lib.GetThing()!;
                    a.ToString();
                    b?.ToString();
                }
            }
            """;
        const string fixedSource = """
            class C
            {
                void M(Lib lib)
                {
                    var (aOrNull, b) = lib.GetThing()!;
                    if (aOrNull is not { } a) return;
                    a.ToString();
                    b?.ToString();
                }
            }
            """;
        return CodeFixVerifier<VariableCodeFixProvider>.VerifyAsync(
            source,
            fixedSource,
            VariableCodeFixProvider.ReturnKey,
            Libs.Oblivious
        );
    }

    [Test]
    public Task Foreach_variable_skips_null_elements()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    foreach (var {|NOA005:e|} in lib.Array!)
                    {
                        e.ToString();
                    }

                    foreach (Foo {|NOA005:f|} in lib.Array!)
                        f.ToString();
                }
            }
            """;
        const string fixedSource = """
            class C
            {
                void M(Lib lib)
                {
                    foreach (var eOrNull in lib.Array!)
                    {
                        if (eOrNull is not { } e) continue;
                        e.ToString();
                    }

                    foreach (Foo fOrNull in lib.Array!)
                    {
                        if (fOrNull is not Foo f) continue;
                        f.ToString();
                    }
                }
            }
            """;
        return CodeFixVerifier<VariableCodeFixProvider>.VerifyAsync(
            source,
            fixedSource,
            VariableCodeFixProvider.ContinueKey,
            Libs.Oblivious
        );
    }

    [Test]
    public Task Lambda_parameter_is_declared_nullable_with_the_delegate_types_written_out()
    {
        const string source = """
            using System;
            class C
            {
                void M(Lib lib)
                {
                    lib.Each({|NOA006:f|} => f.ToString());
                    lib.Changed += ({|NOA006:s|}, e) => s.ToString();
                    FooAction a = ({|NOA006:Foo g|}) => g.ToString();
                }
            }
            """;
        const string fixedSource = """
            using System;
            class C
            {
                void M(Lib lib)
                {
                    lib.Each((Foo? f) => f.ToString());
                    lib.Changed += (object? s, EventArgs e) => s.ToString();
                    FooAction a = (Foo? g) => g.ToString();
                }
            }
            """;
        return CodeFixVerifier<ParameterCodeFixProvider>.VerifyAsync(
            source,
            fixedSource,
            ParameterCodeFixProvider.Key,
            Libs.Oblivious
        );
    }

    [Test]
    public Task Override_parameter_and_property_are_declared_nullable()
    {
        const string source = """
            class D : BaseWithParams
            {
                public override void Take({|NOA007:Foo f|}, int n) { }
            }

            class E : BaseWithProp
            {
                public override {|NOA007:Foo|} Item { get; set; } = new Foo();
            }
            """;
        const string fixedSource = """
            class D : BaseWithParams
            {
                public override void Take(Foo? f, int n) { }
            }

            class E : BaseWithProp
            {
                public override Foo? Item { get; set; } = new Foo();
            }
            """;
        return CodeFixVerifier<ParameterCodeFixProvider>.VerifyAsync(
            source,
            fixedSource,
            ParameterCodeFixProvider.Key,
            Libs.Oblivious
        );
    }

    [Test]
    public Task Nested_positions_of_the_target_type_are_declared_nullable()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            class C
            {
                void Take(IEnumerable<Foo> items) { }
                List<Foo> Ret(Lib lib) => {|NOA009:{|NOA003:lib.Items|}.ToList()|};

                void M(Lib lib)
                {
                    var list = {|NOA003:lib.Items|}.ToList();
                    Foo[] a = {|NOA009:list.ToArray()|};
                    System.Collections.Generic.List<Foo> b = {|NOA009:list|};
                    (Foo, int) c = {|NOA009:lib.GetPair()|};
                    Dictionary<string, List<Foo>> d = new();
                    Take({|NOA009:list|});
                }
            }
            """;
        const string fixedSource = """
            using System.Collections.Generic;
            using System.Linq;
            class C
            {
                void Take(IEnumerable<Foo?> items) { }
                List<Foo?> Ret(Lib lib) => {|NOA003:lib.Items|}.ToList();

                void M(Lib lib)
                {
                    var list = {|NOA003:lib.Items|}.ToList();
                    Foo?[] a = list.ToArray();
                    System.Collections.Generic.List<Foo?> b = list;
                    (Foo?, int) c = lib.GetPair();
                    Dictionary<string, List<Foo>> d = new();
                    Take(list);
                }
            }
            """;
        return CodeFixVerifier<NestedCodeFixProvider>.VerifyAsync(
            source,
            fixedSource,
            NestedCodeFixProvider.Key,
            Libs.Oblivious
        );
    }
}
