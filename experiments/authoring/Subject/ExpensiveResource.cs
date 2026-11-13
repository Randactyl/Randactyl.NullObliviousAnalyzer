namespace Authoring.Subject;

public sealed class ExpensiveResource : IAsyncDisposable
{
    private static int _created;

    private ExpensiveResource()
    {
    }

    public static int Created => Volatile.Read(ref _created);

    public bool IsDisposed { get; private set; }

    public static async Task<ExpensiveResource> CreateAsync()
    {
        await Task.Delay(50);
        Interlocked.Increment(ref _created);

        return new ExpensiveResource();
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;

        return ValueTask.CompletedTask;
    }
}
