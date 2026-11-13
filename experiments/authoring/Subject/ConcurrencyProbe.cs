namespace Authoring.Subject;

public sealed class ConcurrencyProbe
{
    private int _current;

    public static ConcurrencyProbe Sequential { get; } = new();

    public int Current => Volatile.Read(ref _current);

    public IDisposable Enter()
    {
        Interlocked.Increment(ref _current);

        return new Exit(this);
    }

    private sealed class Exit(ConcurrencyProbe probe) : IDisposable
    {
        public void Dispose()
        {
            Interlocked.Decrement(ref probe._current);
        }
    }
}
