using System.Threading.Tasks;
using Randactyl.NullObliviousAnalyzer.Testing;

namespace Randactyl.NullObliviousAnalyzer.Tests;

/// <summary>NOA002 (property/indexer reads) and NOA003 (field reads).</summary>
public class MemberReadTests
{
    [Test]
    public Task Property_and_indexer_reads_from_oblivious_assembly()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    var a = {|NOA002:lib.Prop|};
                    var b = {|NOA002:lib[0]|};
                    var c = lib.IntProp;
                    var d = lib["key"];
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Property_read_from_same_file_disabled_region()
    {
        const string source = """
            #nullable disable
            public class Local { public string Name { get; set; } public object this[int i] => null; }
            #nullable enable
            class C
            {
                void M(Local l)
                {
                    var a = {|NOA002:l.Name|};
                    var b = {|NOA002:l[1]|};
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source);
    }

    [Test]
    public Task Plain_assignment_is_a_write_but_compound_assignment_is_a_read()
    {
        const string source = """
            class C
            {
                void M(Lib lib, Foo f)
                {
                    lib.Prop = f;
                    lib[0] = f;
                    {|NOA002:lib.Prop|} ??= f;
                    var x = new Lib { Prop = f };
                    var y = {|NOA002:lib.Prop|}.ToString();
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Chain_reports_each_oblivious_member()
    {
        const string source = """
            #nullable disable
            public class Node { public Node Next { get; set; } public string Text { get; set; } }
            #nullable enable
            class C
            {
                void M(Node n)
                {
                    var t = {|NOA002:{|NOA002:n.Next|}.Text|};
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source);
    }

    [Test]
    public Task Annotated_members_are_not_reported()
    {
        const string source = """
            using System.Collections.Generic;
            class Annotated { public string Name { get; set; } = ""; public string? Maybe { get; set; } public string? this[int i] => null; }
            class C
            {
                void M(Annotated a, List<string> list, Dictionary<string, string> dict)
                {
                    var x = a.Name;
                    var y = a.Maybe;
                    var z = a[0];
                    var l = list[0];
                    var d = dict["k"];
                    var c = list.Count;
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source);
    }

    [Test]
    public Task Generic_indexer_substituted_with_oblivious_type_argument()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    var f = {|NOA002:{|NOA003:lib.Items|}[0]|};
                    var s = {|NOA002:{|NOA003:lib.Names|}[0]|};
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Nameof_operand_is_never_reported()
    {
        const string source = """
            class C { void M(Lib lib) { var a = nameof(lib.Prop); var b = nameof(Lib.Field); } }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Field_reads_from_oblivious_assembly()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    var a = {|NOA003:lib.Field|};
                    var b = lib.IntField;
                    var c = {|NOA003:{|NOA003:lib.Field|}.Name|};
                    var d = {|NOA003:lib.Factory|};
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Field_read_from_same_file_disabled_region()
    {
        const string source = """
            #nullable disable
            public class Local { public string Text; public static object Shared; }
            #nullable enable
            class C { void M(Local l) { var a = {|NOA003:l.Text|}; var b = {|NOA003:Local.Shared|}; } }
            """;
        return AnalyzerVerifier.VerifyAsync(source);
    }

    [Test]
    public Task Field_writes_are_not_reads()
    {
        const string source = """
            class C { void M(Lib lib, Foo f) { lib.Field = f; var x = new Lib { Field = f }; {|NOA003:lib.Field|} ??= f; } }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Const_fields_are_skipped_only_with_a_non_null_constant()
    {
        const string source = """
            class C
            {
                void M()
                {
                    var text = Lib.ConstText;
                    var nothing = {|NOA003:Lib.ConstNull|};
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Annotated_fields_are_not_reported()
    {
        const string source = """
            class Annotated { public string Text = ""; public string? Maybe; public int N; public int? NullableN; }
            class C { void M(Annotated a) { var x = a.Text; var y = a.Maybe; var z = a.N; var w = a.NullableN; } }
            """;
        return AnalyzerVerifier.VerifyAsync(source);
    }
}
