using System.Threading.Tasks;
using Randactyl.NullObliviousAnalyzer.Testing;
using TUnit.Core;

namespace Randactyl.NullObliviousAnalyzer.Tests;

/// <summary>NOA004 for values that arrive through the out parameters of Deconstruct.</summary>
public class DeconstructionTests
{
    [Test]
    public Task Var_deconstruction_through_instance_and_extension_method()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    var ({|NOA004:a|}, {|NOA004:b|}) = {|NOA001:lib.GetThing()|};
                    var ({|NOA004:c|}, n) = {|NOA001:lib.GetTriple()|};
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Deconstruction_into_existing_and_explicitly_typed_targets()
    {
        const string source = """
            class C
            {
                Foo field = new Foo();

                void M(Lib lib)
                {
                    Foo x = new Foo();
                    Foo? maybe = null;
                    ({|NOA004:x|}, maybe) = {|NOA001:lib.GetThing()|};
                    ({|NOA004:field|}, _) = {|NOA001:lib.GetThing()|};
                    (Foo {|NOA004:a|}, Foo? b) = {|NOA001:lib.GetThing()|};
                    (Foo {|NOA004:c|}, var {|NOA004:d|}) = {|NOA001:lib.GetThing()|};
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Discards_are_not_reported()
    {
        const string source = """
            class C { void M(Lib lib) { var (_, {|NOA004:b|}) = {|NOA001:lib.GetThing()|}; var (_, _) = {|NOA001:lib.GetThing()|}; } }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Foreach_deconstruction_reports_each_target_and_the_element()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    foreach ({|NOA005:var ({|NOA004:a|}, {|NOA004:b|})|} in {|NOA003:lib.Things|}) { }
                    foreach ({|NOA005:var ({|NOA004:c|}, n)|} in {|NOA003:lib.Triples|}) { }
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Tuples_are_left_to_NOA008_and_annotated_deconstructors_are_not_reported()
    {
        const string source = """
            class Pair { public void Deconstruct(out string a, out string b) { a = ""; b = ""; } }
            class C
            {
                void M(Lib lib, Pair pair)
                {
                    var (a, n) = {|NOA008:lib.GetPair()|};
                    var (b, c) = pair;
                    var (d, e) = (1, 2);
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Nested_deconstruction_reaches_the_inner_deconstructor()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    var (n, ({|NOA004:a|}, {|NOA004:b|})) = (1, {|NOA001:lib.GetThing()|});
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }
}
