using System.Threading.Tasks;
using Randactyl.NullObliviousAnalyzer.Testing;

namespace Randactyl.NullObliviousAnalyzer.Tests;

/// <summary>
/// A variable declared where an oblivious value arrives (out var, foreach, deconstruction, lambda parameter) is
/// handled when it is never written again and every read of it handles null.
/// </summary>
public class HandledVariableTests
{
    [Test]
    public Task Out_variable_is_handled_when_every_read_handles_null()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    lib.Fill(out var raw);
                    if (raw is not { } a) return;
                    lib.Fill(out Foo other);
                    var n = other?.Name;
                    lib.Fill(out var unused);
                    lib.Fill({|NOA004:out var bad|});
                    var m = bad.Count;
                    lib.Fill({|NOA004:out var mixed|});
                    if (mixed is null) return;
                    var k = mixed.Count;
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Existing_and_reassigned_variables_are_not_handled()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    Foo? existing;
                    lib.Fill({|NOA004:out existing|});
                    var a = existing?.Name;
                    lib.Fill({|NOA004:out var again|});
                    again = new Foo();
                    var b = again?.Name;
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Foreach_variable_is_handled_when_every_read_handles_null()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    foreach (var e in lib.Array!)
                    {
                        if (e is not { } f) continue;
                    }
                    foreach (var {|NOA005:g|} in lib.Array!)
                    {
                        var n = g.Count;
                    }
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Deconstructed_variables_are_handled_one_by_one()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    var (a, b) = lib.GetThing()!;
                    if (a is null || b is null) return;
                    var ({|NOA004:c|}, d) = lib.GetThing()!;
                    var n = c.Count;
                    var m = d?.Name;
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Tuple_elements_are_reported_per_target_and_handled_like_other_variables()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    var ({|NOA004:a|}, n) = {|NOA008:lib.GetPair()|};
                    var x = a.Count;
                    var (b, m) = {|NOA008:lib.GetPair()|};
                    var y = b?.Name;
                    (Foo? {|NOA004:c|}, int k) = {|NOA008:lib.GetPair()|};
                    var z = c.Count;
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Tuple_literal_elements_are_reported_where_they_are_produced()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    var (a, n) = ({|NOA001:lib.GetFoo()|}, 1);
                    var x = a.Count;
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Lambda_parameter_is_handled_when_every_read_handles_null()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    lib.Each(f => System.Console.WriteLine(f?.Name));
                    lib.Each(f => { });
                    lib.Changed += (s, e) => { };
                    lib.Each({|NOA006:g|} => System.Console.WriteLine(g.Name));
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }
}
