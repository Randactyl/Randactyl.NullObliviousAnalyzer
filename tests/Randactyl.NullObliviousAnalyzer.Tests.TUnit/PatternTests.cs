using System.Threading.Tasks;
using Randactyl.NullObliviousAnalyzer.Testing;
using TUnit.Core;

namespace Randactyl.NullObliviousAnalyzer.Tests;

/// <summary>Members read by patterns are reads like any other.</summary>
public class PatternTests
{
    [Test]
    public Task Property_pattern_reads_a_field()
    {
        const string source = """
            class C { void M(Foo x) { if (x is { {|NOA003:Name|}: var n }) { } } }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Extended_property_pattern_reads_each_member_of_the_chain()
    {
        const string source = """
            class C { void M(Wrapper w) { if (w is { {|NOA003:{|NOA003:Inner|}.Name|}: "a" }) { } } }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Switch_expression_arms_read_members()
    {
        const string source = """
            class C
            {
                int M(Wrapper w) => w switch
                {
                    { {|NOA003:Label|}: "x" } => 1,
                    { {|NOA003:Inner|}: null } => 2,
                    _ => 3,
                };
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Property_pattern_on_a_property_and_value_member_reads()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    if (lib is { {|NOA002:Prop|}: not null, IntProp: 1 }) { }
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }
}
