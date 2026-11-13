using System.Threading.Tasks;
using Randactyl.NullObliviousAnalyzer.Testing;
using TUnit.Core;

namespace Randactyl.NullObliviousAnalyzer.Tests;

/// <summary>NOA008.</summary>
public class NestedObliviousTests
{
    [Test]
    public Task Tuple_and_value_task_results_from_oblivious_assembly()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    var pair = {|NOA008:lib.GetPair()|};
                    var task = {|NOA008:lib.GetFooValueAsync()|};
                    var p = {|NOA008:lib.PairProp|};
                    var f = {|NOA008:lib.PairField|};
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Generic_substitution_produces_nested_array_or_list()
    {
        const string source = """
            using System.Linq;
            class C
            {
                void M(Lib lib)
                {
                    var arr = {|NOA008:{|NOA003:lib.Items|}.ToArray()|};
                    var list = {|NOA008:{|NOA003:lib.Items|}.ToList()|};
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Out_argument_with_nested_oblivious_type()
    {
        const string source = """
            class C { void M(Lib lib) { lib.FillPair({|NOA008:out var p|}); } }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Directly_awaited_operand_is_not_reported_when_the_await_is()
    {
        const string source = """
            using System.Threading.Tasks;
            class C { async Task M(Lib lib) { var f = {|NOA001:await lib.GetFooValueAsync()|}; } }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Directly_iterated_operand_is_not_reported_when_the_element_is()
    {
        const string source = """
            using System.Linq;
            class C { void M(Lib lib) { foreach (var {|NOA005:x|} in {|NOA003:lib.Items|}.ToArray()) { } } }
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
                    var a = list.ToArray();
                    var b = list.ToList();
                    var c = pair;
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source);
    }
}
