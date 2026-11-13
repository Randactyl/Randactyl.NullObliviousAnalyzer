namespace Randactyl.NullObliviousAnalyzer.Tests;

/// <summary>Sources for the separate nullable-disabled project that every cross-assembly test references.</summary>
internal static class Libs
{
    public const string Oblivious = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;

        public class Foo { public string Name; public int Count; }
        public struct Val { }
        public delegate void Handler(object sender, EventArgs e);
        public delegate void FooAction(Foo f);
        public delegate Foo FooFactory();

        public class Lib
        {
            public Foo GetFoo() => null;
            public static Foo StaticFoo() => null;
            public int GetInt() => 0;
            public int? GetNullableInt() => null;
            public Val GetVal() => default;
            public string GetString() => null;

            public Foo Prop { get; set; }
            public int IntProp { get; set; }
            public Foo this[int i] { get => null; set { } }
            public int this[string key] => 0;

            public Foo Field;
            public int IntField;
            public const string ConstNull = null;
            public const string ConstText = "text";

            public Foo[] Array;
            public int[] Ints;
            public List<Foo> Items;
            public List<string> Names;

            public Task<Foo> GetFooAsync() => null;
            public ValueTask<Foo> GetFooValueAsync() => default;
            public Task<int> GetIntAsync() => null;
            public (Foo, int) GetPair() => default;
            public (Foo, int) PairProp { get; }
            public (Foo, int) PairField;

            public void Fill(out Foo f) { f = null; }
            public void Swap(ref Foo f) { }
            public void FillInt(out int i) { i = 0; }
            public void FillPair(out (Foo, int) p) { p = default; }

            public event Handler Changed;
            public Handler HandlerField;
            public FooFactory Factory;
            public void Subscribe(Handler h) { }
            public void SubscribeFoo(FooAction a) { }
            public void Each(Action<Foo> a) { }

            public T Resolve<T>() => default;
            public Task<T> ResolveAsync<T>() => null;
            public ValueTask<T> ResolveValueAsync<T>() => default;
            public (T, int) ResolvePair<T>() => default;
            public T[] ResolveArray<T>() => null;
            public T ResolveClass<T>() where T : class => default;
            public List<T> ListOf<T>() => null;
            public IEnumerable<T> Stream<T>() => null;
            public bool TryResolve<T>(out T value) { value = default; return false; }
            public void Listen<T>(Handler<T> h) { }
            public Box<Foo> FooBox;
            public Box<int> IntBox;
            public Pair GetThing() => null;
            public List<Pair> Things;
            public Triple GetTriple() => null;
            public List<Triple> Triples;
            public IAsyncEnumerable<Foo> StreamAsync() => null;

            public static Foo operator +(Lib a, Lib b) => null;
            public static implicit operator Foo(Lib l) => null;
        }

        public delegate void Handler<T>(T item);

        public class Box<T>
        {
            public T Value;
            public T Prop { get; set; }
            public T this[int i] { get => default; set { } }
            public T Get() => default;
            public bool TryGet(out T value) { value = default; return false; }
        }

        public class Pair { public void Deconstruct(out Foo a, out Foo b) { a = null; b = null; } }
        public class Triple { }
        public static class TripleExtensions { public static void Deconstruct(this Triple t, out Foo a, out int n) { a = null; n = 0; } }

        public abstract class BaseWithParams
        {
            public virtual void Take(Foo f, int n) { }
        }

        public class Wrapper { public Foo Inner; public string Label; }
        public class BaseWithIndexer { public virtual Foo this[Foo key] { get => null; set { } } }
        public interface IAccepts { void Accept(Foo f); }
        public interface IAcceptsGeneric<T> { void Accept(T t); }
        public interface IHasFoo { Foo Current { get; set; } Foo ReadOnly { get; } Foo this[Foo key] { get; } }
        public interface IHasGeneric<T> { T Item { get; set; } }
        public class BaseWithProp { public virtual Foo Item { get; set; } public virtual Foo ReadOnly { get; } public virtual Foo InitOnly { get; init; } }
        """;
}
