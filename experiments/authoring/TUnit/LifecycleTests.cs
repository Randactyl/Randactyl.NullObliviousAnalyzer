namespace Authoring.Tests;

public static class AssemblyHooks
{
    public static string State { get; private set; } = "";

    [Before(Assembly)]
    public static void Start()
    {
        State = "assembly";
    }

    [After(Assembly)]
    public static void Stop()
    {
        State = "";
    }
}

public class LifecycleTests
{
    private static string _classState = "";
    private readonly MemoryStream _stream = new();

    [Before(Class)]
    public static void StartClass()
    {
        _classState = "class";
    }

    [After(Class)]
    public static void StopClass()
    {
        _classState = "";
    }

    [Before(Test)]
    public async Task OpenStream(CancellationToken cancellationToken)
    {
        await _stream.WriteAsync("ready"u8.ToArray(), cancellationToken);
    }

    [After(Test)]
    public async Task CloseStream()
    {
        await _stream.DisposeAsync();
    }

    [Test]
    public async Task First_test_sees_every_level()
    {
        await Assert.That(_stream.Length).IsEqualTo(5);
        await Assert.That(_classState).IsEqualTo("class");
        await Assert.That(AssemblyHooks.State).IsEqualTo("assembly");
    }

    [Test]
    public async Task Second_test_sees_every_level()
    {
        await Assert.That(_stream.Length).IsEqualTo(5);
        await Assert.That(_classState).IsEqualTo("class");
        await Assert.That(AssemblyHooks.State).IsEqualTo("assembly");
    }
}
