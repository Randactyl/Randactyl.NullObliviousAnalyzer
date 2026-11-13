using System.Threading.Tasks;
using Randactyl.NullObliviousAnalyzer.Testing;

namespace Randactyl.NullObliviousAnalyzer.Tests;

public class ConfigurationTests
{
    private const string Vendor = """
        namespace Vendor.Inner { public class Thing { public string Get() => null; } }
        namespace Vendor { public class Top { public string Get() => null; } }
        namespace Other { public class Else { public string Get() => null; } }
        namespace Vendor { public class Holder { public System.Collections.Generic.List<Other.Else> Items; } }
        """;

    [Test]
    public Task Excluded_assembly_is_not_reported()
    {
        const string source = """
            class C { void M(Lib lib) { var a = lib.GetFoo(); var b = lib.Field; } }
            """;
        return AnalyzerVerifier.VerifyAsync(
            source,
            Libs.Oblivious,
            o => o.EditorConfig = "null_oblivious_analyzer.excluded_assemblies = ObliviousLib"
        );
    }

    [Test]
    public Task Excluded_assembly_with_prefix_wildcard_and_whitespace()
    {
        const string source = """
            class C { void M(Lib lib) { var a = lib.GetFoo(); } }
            """;
        return AnalyzerVerifier.VerifyAsync(
            source,
            Libs.Oblivious,
            o =>
                o.EditorConfig =
                    "null_oblivious_analyzer.excluded_assemblies =  System.* ,  Oblivious* "
        );
    }

    [Test]
    public Task Non_matching_assembly_exclusion_still_reports()
    {
        const string source = """
            class C { void M(Lib lib) { var a = {|NOA001:lib.GetFoo()|}; } }
            """;
        return AnalyzerVerifier.VerifyAsync(
            source,
            Libs.Oblivious,
            o => o.EditorConfig = "null_oblivious_analyzer.excluded_assemblies = Obliviou, Other.*"
        );
    }

    [Test]
    public Task Global_config_is_used_as_fallback()
    {
        const string source = """
            class C { void M(Lib lib) { var a = lib.GetFoo(); } }
            """;
        return AnalyzerVerifier.VerifyAsync(
            source,
            Libs.Oblivious,
            o => o.GlobalConfig = "null_oblivious_analyzer.excluded_assemblies = ObliviousLib"
        );
    }

    [Test]
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
        return AnalyzerVerifier.VerifyAsync(
            source,
            Vendor,
            o => o.EditorConfig = "null_oblivious_analyzer.excluded_namespaces = Vendor"
        );
    }

    [Test]
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
        return AnalyzerVerifier.VerifyAsync(
            source,
            Vendor,
            o => o.EditorConfig = "null_oblivious_analyzer.excluded_namespaces = Vendor.Inner"
        );
    }

    [Test]
    public Task Namespace_exclusion_is_decided_by_the_member_not_the_type_argument()
    {
        // Items is in Vendor (excluded); ToArray is in System.Collections.Generic and merely returns an oblivious Other.Else[].
        const string source = """
            using System.Linq;
            class C { void M(Vendor.Holder h) { var a = {|NOA008:h.Items.ToArray()|}; } }
            """;
        return AnalyzerVerifier.VerifyAsync(
            source,
            Vendor,
            o => o.EditorConfig = "null_oblivious_analyzer.excluded_namespaces = Vendor, Other"
        );
    }

    [Test]
    public Task Assembly_exclusion_is_decided_by_the_member_not_the_type_argument()
    {
        const string source = """
            using System.Linq;
            class C { void M(Lib lib) { var a = lib.Items.ToArray(); } }
            """;
        return AnalyzerVerifier.VerifyAsync(
            source,
            Libs.Oblivious,
            o =>
                o.EditorConfig =
                    "null_oblivious_analyzer.excluded_assemblies = System.*, ObliviousLib"
        );
    }

    [Test]
    public Task Excluded_assembly_applies_to_overrides()
    {
        const string source = """
            class Impl : BaseWithParams { public override void Take(Foo f, int n) { } }
            """;
        return AnalyzerVerifier.VerifyAsync(
            source,
            Libs.Oblivious,
            o => o.EditorConfig = "null_oblivious_analyzer.excluded_assemblies = ObliviousLib"
        );
    }

    private const string VendorSource = """
        namespace Vendor
        {
            public class Source
            {
                public string[] Names;
                public System.Collections.Generic.List<string> Items;
                public System.Threading.Tasks.Task<string> GetAsync() => null;
            }
        }
        """;

    private const string ArrayCallback = """

        public delegate void ArrayCallback(Foo[] a);
        public class Registry
        {
            public void On(ArrayCallback c) { }
            public System.Threading.Tasks.Task<Foo[]> LoadAsync() => null;
        }
        """;

    private const string ExcludeLib = "null_oblivious_analyzer.excluded_assemblies = ObliviousLib";
    private const string ExcludeVendor = "null_oblivious_analyzer.excluded_namespaces = Vendor";

    [Test]
    public Task Excluded_assembly_follows_a_local_into_foreach()
    {
        const string source = """
            class C { void M(Lib lib) { var arr = lib.Array; foreach (var e in arr) { } } }
            """;
        return AnalyzerVerifier.VerifyAsync(
            source,
            Libs.Oblivious,
            o => o.EditorConfig = ExcludeLib
        );
    }

    [Test]
    public Task Excluded_assembly_follows_a_local_into_array_element()
    {
        const string source = """
            class C { void M(Lib lib) { var arr = lib.Array; var x = arr[0]; } }
            """;
        return AnalyzerVerifier.VerifyAsync(
            source,
            Libs.Oblivious,
            o => o.EditorConfig = ExcludeLib
        );
    }

    [Test]
    public Task Excluded_assembly_follows_a_local_into_spread()
    {
        const string source = """
            using System.Collections.Generic;
            class C { void M(Lib lib) { var items = lib.Items; List<Foo> copy = [.. items]; } }
            """;
        return AnalyzerVerifier.VerifyAsync(
            source,
            Libs.Oblivious,
            o => o.EditorConfig = ExcludeLib
        );
    }

    [Test]
    public Task Excluded_assembly_follows_a_local_into_await()
    {
        const string source = """
            class C { async System.Threading.Tasks.Task M(Lib lib) { var t = lib.GetFooAsync(); var f = await t; } }
            """;
        return AnalyzerVerifier.VerifyAsync(
            source,
            Libs.Oblivious,
            o => o.EditorConfig = ExcludeLib
        );
    }

    [Test]
    public Task Excluded_assembly_follows_locals_through_parentheses_conversions_and_other_locals()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    var first = lib.Array;
                    var second = (first);
                    var third = second;
                    foreach (var e in third) { }
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(
            source,
            Libs.Oblivious,
            o => o.EditorConfig = ExcludeLib
        );
    }

    [Test]
    public Task Excluded_assembly_follows_a_local_initialized_with_an_await()
    {
        const string source = """
            class C { async System.Threading.Tasks.Task M(Registry registry) { var arr = await registry.LoadAsync(); var x = arr[0]; } }
            """;
        return AnalyzerVerifier.VerifyAsync(
            source,
            Libs.Oblivious + ArrayCallback,
            o => o.EditorConfig = ExcludeLib
        );
    }

    [Test]
    public Task Excluded_namespace_follows_a_local_into_foreach()
    {
        const string source = """
            class C { void M(Vendor.Source s) { var arr = s.Names; foreach (var e in arr) { } } }
            """;
        return AnalyzerVerifier.VerifyAsync(
            source,
            VendorSource,
            o => o.EditorConfig = ExcludeVendor
        );
    }

    [Test]
    public Task Excluded_namespace_follows_a_local_into_array_element()
    {
        const string source = """
            class C { void M(Vendor.Source s) { var arr = s.Names; var x = arr[0]; } }
            """;
        return AnalyzerVerifier.VerifyAsync(
            source,
            VendorSource,
            o => o.EditorConfig = ExcludeVendor
        );
    }

    [Test]
    public Task Excluded_namespace_follows_a_local_into_spread()
    {
        const string source = """
            using System.Collections.Generic;
            class C { void M(Vendor.Source s) { var items = s.Items; List<string> copy = [.. items]; } }
            """;
        return AnalyzerVerifier.VerifyAsync(
            source,
            VendorSource,
            o => o.EditorConfig = ExcludeVendor
        );
    }

    [Test]
    public Task Excluded_namespace_follows_a_local_into_await()
    {
        const string source = """
            class C { async System.Threading.Tasks.Task M(Vendor.Source s) { var t = s.GetAsync(); var v = await t; } }
            """;
        return AnalyzerVerifier.VerifyAsync(
            source,
            VendorSource,
            o => o.EditorConfig = ExcludeVendor
        );
    }

    [Test]
    public Task Local_that_is_reassigned_is_still_reported()
    {
        const string source = """
            class C
            {
                void M(Lib lib)
                {
                    var arr = lib.Array;
                    arr = lib.Array;
                    foreach (var {|NOA005:e|} in arr) { }
                    var x = {|NOA005:arr[0]|};
                }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(
            source,
            Libs.Oblivious,
            o => o.EditorConfig = ExcludeLib
        );
    }

    [Test]
    public Task Parameter_origin_cannot_be_determined_and_is_still_reported()
    {
        const string source = """
            class C { void M(Registry registry) { registry.On(arr => { foreach (var {|NOA005:e|} in arr) { } }); } }
            """;
        return AnalyzerVerifier.VerifyAsync(
            source,
            Libs.Oblivious + ArrayCallback,
            o => o.EditorConfig = ExcludeLib
        );
    }

    [Test]
    public Task Assembly_wildcard_with_dot_matches_the_bare_prefix_and_children_but_not_a_longer_name()
    {
        const string source = """
            class C { void M(Lib lib) { var a = lib.GetFoo(); } }
            """;
        const string reported = """
            class C { void M(Lib lib) { var a = {|NOA001:lib.GetFoo()|}; } }
            """;
        const string config = "null_oblivious_analyzer.excluded_assemblies = System.*";
        return Task.WhenAll(
            AnalyzerVerifier.VerifyAsync(
                source,
                Libs.Oblivious,
                o =>
                {
                    o.ObliviousLibraryName = "System";
                    o.EditorConfig = config;
                }
            ),
            AnalyzerVerifier.VerifyAsync(
                source,
                Libs.Oblivious,
                o =>
                {
                    o.ObliviousLibraryName = "System.Xml";
                    o.EditorConfig = config;
                }
            ),
            AnalyzerVerifier.VerifyAsync(
                reported,
                Libs.Oblivious,
                o =>
                {
                    o.ObliviousLibraryName = "SystemFoo";
                    o.EditorConfig = config;
                }
            )
        );
    }

    [Test]
    public Task Assembly_wildcard_without_dot_is_a_plain_prefix()
    {
        const string source = """
            class C { void M(Lib lib) { var a = lib.GetFoo(); } }
            """;
        const string config = "null_oblivious_analyzer.excluded_assemblies = Sys*";
        return Task.WhenAll(
            AnalyzerVerifier.VerifyAsync(
                source,
                Libs.Oblivious,
                o =>
                {
                    o.ObliviousLibraryName = "SystemFoo";
                    o.EditorConfig = config;
                }
            ),
            AnalyzerVerifier.VerifyAsync(
                source,
                Libs.Oblivious,
                o =>
                {
                    o.ObliviousLibraryName = "System";
                    o.EditorConfig = config;
                }
            )
        );
    }

    [Test]
    public Task Assembly_names_match_without_regard_to_case()
    {
        const string source = """
            class C { void M(Lib lib) { var a = lib.GetFoo(); } }
            """;
        return Task.WhenAll(
            AnalyzerVerifier.VerifyAsync(
                source,
                Libs.Oblivious,
                o => o.EditorConfig = "null_oblivious_analyzer.excluded_assemblies = obliviouslib"
            ),
            AnalyzerVerifier.VerifyAsync(
                source,
                Libs.Oblivious,
                o => o.EditorConfig = "null_oblivious_analyzer.excluded_assemblies = OBLIVIOUS*"
            )
        );
    }

    [Test]
    public Task Namespace_names_match_without_regard_to_case()
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
        return Task.WhenAll(
            AnalyzerVerifier.VerifyAsync(
                source,
                Vendor,
                o => o.EditorConfig = "null_oblivious_analyzer.excluded_namespaces = vendor"
            ),
            AnalyzerVerifier.VerifyAsync(
                source,
                Vendor,
                o => o.EditorConfig = "null_oblivious_analyzer.excluded_namespaces = VENDOR.*"
            )
        );
    }

    [Test]
    public Task Namespace_wildcard_without_dot_is_not_a_prefix_match()
    {
        const string source = """
            class C { void M(Vendor.Top b) { var y = {|NOA001:b.Get()|}; } }
            """;
        return AnalyzerVerifier.VerifyAsync(
            source,
            Vendor,
            o => o.EditorConfig = "null_oblivious_analyzer.excluded_namespaces = Ven*"
        );
    }

    [Test]
    public Task Namespace_wildcard_with_dot_covers_the_namespace_itself()
    {
        const string source = """
            class C { void M(Vendor.Top b, Vendor.Inner.Thing t, Other.Else c) { var x = b.Get(); var y = t.Get(); var z = {|NOA001:c.Get()|}; } }
            """;
        return AnalyzerVerifier.VerifyAsync(
            source,
            Vendor,
            o => o.EditorConfig = "null_oblivious_analyzer.excluded_namespaces = Vendor.*"
        );
    }
}
