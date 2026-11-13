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

            public static Foo operator +(Lib a, Lib b) => null;
            public static implicit operator Foo(Lib l) => null;
        }

        public abstract class BaseWithParams
        {
            public virtual void Take(Foo f, int n) { }
        }

        public interface IAccepts { void Accept(Foo f); }
        public interface IAcceptsGeneric<T> { void Accept(T t); }
        """;
}
