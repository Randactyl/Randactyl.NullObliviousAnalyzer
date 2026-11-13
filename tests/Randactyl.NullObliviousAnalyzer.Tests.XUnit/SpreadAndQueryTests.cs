using System.Threading.Tasks;
using Randactyl.NullObliviousAnalyzer.Testing;
using Xunit;

namespace Randactyl.NullObliviousAnalyzer.Tests;

/// <summary>NOA005 for collection-expression spreads, LINQ range variables and await foreach.</summary>
public class SpreadAndQueryTests
{
    [Fact]
    public Task Spread_of_oblivious_elements_into_an_annotated_collection()
    {
        const string source = """
            using System.Collections.Generic;
            class C
            {
                void M(Lib lib)
                {
                    List<Foo> a = [{|NOA005:.. {|NOA003:lib.Items|}|}];
                    Foo[] b = [{|NOA005:.. {|NOA003:lib.Array|}|}];
                    List<string> c = [{|NOA005:.. {|NOA003:lib.Names|}|}];
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Directly_spread_nested_operand_is_not_reported_by_NOA008()
    {
        const string source = """
            using System.Linq;
            class C
            {
                void M(Lib lib)
                {
                    Foo[] a = [{|NOA005:.. {|NOA003:lib.Items|}.ToArray()|}];
                    var b = {|NOA008:{|NOA003:lib.Items|}.ToArray()|};
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Spread_into_nullable_elements_or_of_annotated_elements_is_not_reported()
    {
        const string source = """
            using System.Collections.Generic;
            class C
            {
                void M(Lib lib, List<string> names, Foo[] foos)
                {
                    List<Foo?> a = [.. {|NOA003:lib.Items|}];
                    List<string> b = [.. names];
                    List<Foo> c = [.. foos];
                    List<int> d = [.. {|NOA003:lib.Ints|}];
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Query_range_variable_is_reported_once_on_its_identifier()
    {
        const string source = """
            using System.Linq;
            class C
            {
                void M(Lib lib)
                {
                    var q = from {|NOA005:p|} in {|NOA003:lib.Items|} select {|NOA003:p.Name|};
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Query_clauses_do_not_report_their_generated_lambdas()
    {
        const string source = """
            using System.Linq;
            class C
            {
                void M(Lib lib)
                {
                    var q = from {|NOA005:p|} in {|NOA003:lib.Items|}
                            where p.Count > 0
                            let n = p.Count + 1
                            orderby p.Count
                            select p.Count + n;
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Query_with_several_from_clauses_and_a_join()
    {
        const string source = """
            using System.Linq;
            class C
            {
                void M(Lib lib, string[] names)
                {
                    var a = from {|NOA005:p|} in {|NOA003:lib.Items|} from n in names select n.Length;
                    var b = from n in names join {|NOA005:p|} in {|NOA003:lib.Items|} on n equals "x" select n.Length;
                    var c = from n in names select n.Length;
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Fact]
    public Task Await_foreach_over_oblivious_async_enumerable()
    {
        const string source = """
            using System.Threading.Tasks;
            class C
            {
                async Task M(Lib lib)
                {
                    await foreach (var {|NOA005:x|} in {|NOA001:lib.StreamAsync()|}) { }
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }
}
