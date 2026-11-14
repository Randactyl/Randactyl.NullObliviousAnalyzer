using System.Threading.Tasks;
using Randactyl.NullObliviousAnalyzer.Testing;

namespace Randactyl.NullObliviousAnalyzer.Tests;

/// <summary>Behaviours that follow from reading declared symbols, pinned so a change is noticed.</summary>
public class PinnedBehaviorTests
{
    [Test]
    public Task A_null_check_handles_only_the_value_it_checks_because_there_is_no_flow_analysis()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    if (lib.Prop != null)
                    {
                        var x = {|NOA002:lib.Prop|};
                    }
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Var_locals_carry_obliviousness_and_explicitly_typed_locals_are_reported_where_assigned()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    var inferred = {|NOA003:lib.Array|};
                    var a = {|NOA005:inferred[0]|};
                    Foo[] declared = {|NOA009:{|NOA003:lib.Array|}|};
                    var b = declared[0];
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Type_argument_inferred_from_an_oblivious_argument_is_not_oblivious()
    {
        // The compiler's inference turns Task.FromResult(<oblivious>) into Task<Foo> with a not-null Foo,
        // so only the argument is reported. This is a limit of what Roslyn exposes, not a choice.
        const string source = """
            using System.Threading.Tasks;
            class C
            {
                async Task M(Lib lib)
                {
                    var task = Task.FromResult({|NOA001:lib.GetFoo()|});
                    var foo = await task;
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Await_of_a_var_holding_an_oblivious_task_is_reported()
    {
        const string source = """
            using System.Threading.Tasks;
            class C
            {
                async Task M(Lib lib)
                {
                    var task = {|NOA001:lib.GetFooAsync()|};
                    var foo = {|NOA001:await task|};
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Converting_to_a_type_without_nested_positions_is_not_reported()
    {
        // object has no element type to compare, and the cast back is a type the developer declares.
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            class C
            {
                void M(Lib lib)
                {
                    var list = {|NOA003:lib.Items|}.ToList();
                    object boxed = list;
                    var back = (List<Foo>)boxed;
                    back[0].ToString();
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }
}
