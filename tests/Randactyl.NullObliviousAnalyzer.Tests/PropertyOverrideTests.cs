using System.Threading.Tasks;
using Randactyl.NullObliviousAnalyzer.Testing;

namespace Randactyl.NullObliviousAnalyzer.Tests;

/// <summary>NOA007 for properties and indexers: the setter value and the indexer parameters come in from callers of the base.</summary>
public class PropertyOverrideTests
{
    [Test]
    public Task Override_of_oblivious_property_with_setter_or_init()
    {
        const string source = """
            class Impl : BaseWithProp
            {
                public override {|NOA007:Foo|} Item { get => new Foo(); set { } }
                public override {|NOA007:Foo|} InitOnly { get => new Foo(); init { } }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Read_only_and_nullable_overrides_are_not_reported()
    {
        const string source = """
            class Impl : BaseWithProp
            {
                public override Foo? Item { get => null; set { } }
                public override Foo ReadOnly => new Foo();
                public override Foo? InitOnly { get => null; init { } }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Interface_implementation_implicit_explicit_and_generic()
    {
        const string source = """
            class Implicit : IHasFoo
            {
                public {|NOA007:Foo|} Current { get => new Foo(); set { } }
                public Foo ReadOnly => new Foo();
                public Foo this[{|NOA007:Foo key|}] => new Foo();
            }
            class Explicit : IHasFoo
            {
                {|NOA007:Foo|} IHasFoo.Current { get => new Foo(); set { } }
                Foo IHasFoo.ReadOnly => new Foo();
                Foo IHasFoo.this[{|NOA007:Foo key|}] => new Foo();
            }
            class Generic : IHasGeneric<Foo>
            {
                public {|NOA007:Foo|} Item { get => new Foo(); set { } }
            }
            class Nullable : IHasGeneric<Foo?>
            {
                public Foo? Item { get => null; set { } }
            }
            class Value : IHasGeneric<int>
            {
                public int Item { get; set; }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Indexer_override_reports_the_type_and_each_parameter_once()
    {
        const string source = """
            class Impl : BaseWithIndexer
            {
                public override {|NOA007:Foo|} this[{|NOA007:Foo key|}] { get => new Foo(); set { } }
            }
            class Nullable : BaseWithIndexer
            {
                public override Foo? this[Foo? key] { get => null; set { } }
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source, Libs.Oblivious);
    }

    [Test]
    public Task Override_of_property_in_same_file_disabled_region()
    {
        const string source = """
            #nullable disable
            public class Base { public virtual string Name { get; set; } public virtual string Fixed { get; } }
            #nullable enable
            class Derived : Base
            {
                public override {|NOA007:string|} Name { get => ""; set { } }
                public override string Fixed => "";
            }
            """;
        return AnalyzerVerifier.VerifyAsync(source);
    }
}
