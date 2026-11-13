using System.Threading.Tasks;
using Randactyl.NullObliviousAnalyzer.Testing;
using Xunit;

namespace Randactyl.NullObliviousAnalyzer.Tests;

public class ConfigurationTests
{
    private const string Vendor = """
        namespace Vendor.Inner { public class Thing { public string Get() => null; } }
        namespace Vendor { public class Top { public string Get() => null; } }
        namespace Other { public class Else { public string Get() => null; } }
        namespace Vendor { public class Holder { public System.Collections.Generic.List<Other.Else> Items; } }
        """;

    [Fact]
    public Task Excluded_assembly_is_not_reported()
    {
        const string source = """
            class C { void M(Lib lib) { var a = lib.GetFoo(); var b = lib.Field; } }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious, o => o.EditorConfig = "null_oblivious_analyzer.excluded_assemblies = ObliviousLib");
    }

    [Fact]
    public Task Excluded_assembly_with_prefix_wildcard_and_whitespace()
    {
        const string source = """
            class C { void M(Lib lib) { var a = lib.GetFoo(); } }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious, o => o.EditorConfig = "null_oblivious_analyzer.excluded_assemblies =  System.* ,  Oblivious* ");
    }

    [Fact]
    public Task Non_matching_assembly_exclusion_still_reports()
    {
        const string source = """
            class C { void M(Lib lib) { var a = {|NOA001:lib.GetFoo()|}; } }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious, o => o.EditorConfig = "null_oblivious_analyzer.excluded_assemblies = Obliviou, Other.*");
    }

    [Fact]
    public Task Global_config_is_used_as_fallback()
    {
        const string source = """
            class C { void M(Lib lib) { var a = lib.GetFoo(); } }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious, o => o.GlobalConfig = "null_oblivious_analyzer.excluded_assemblies = ObliviousLib");
    }

    [Fact]
    public Task Excluded_namespace_includes_child_namespaces()
    {
        const string source = """
            class C
            {
                void M(Vendor.Inner.Thing a, Vendor.Top b, Other.Else c)
                {
                    var x = a.Get();
                    var y = b.Get();
                    var z = {|NOA001:c.Get()|};
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Vendor, o => o.EditorConfig = "null_oblivious_analyzer.excluded_namespaces = Vendor");
    }

    [Fact]
    public Task Excluded_child_namespace_does_not_exclude_its_parent()
    {
        const string source = """
            class C
            {
                void M(Vendor.Inner.Thing a, Vendor.Top b)
                {
                    var x = a.Get();
                    var y = {|NOA001:b.Get()|};
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Vendor, o => o.EditorConfig = "null_oblivious_analyzer.excluded_namespaces = Vendor.Inner");
    }

    [Fact]
    public Task Namespace_exclusion_is_decided_by_the_member_not_the_type_argument()
    {
        // Items is in Vendor (excluded); ToArray is in System.Collections.Generic and merely returns an oblivious Other.Else[].
        const string source = """
            using System.Linq;
            class C { void M(Vendor.Holder h) { var a = {|NOA008:h.Items.ToArray()|}; } }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Vendor, o => o.EditorConfig = "null_oblivious_analyzer.excluded_namespaces = Vendor, Other");
    }

    [Fact]
    public Task Assembly_exclusion_is_decided_by_the_member_not_the_type_argument()
    {
        const string source = """
            using System.Linq;
            class C { void M(Lib lib) { var a = lib.Items.ToArray(); } }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious, o => o.EditorConfig = "null_oblivious_analyzer.excluded_assemblies = System.*, ObliviousLib");
    }

    [Fact]
    public Task Excluded_assembly_applies_to_overrides()
    {
        const string source = """
            class Impl : BaseWithParams { public override void Take(Foo f, int n) { } }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious, o => o.EditorConfig = "null_oblivious_analyzer.excluded_assemblies = ObliviousLib");
    }
}
