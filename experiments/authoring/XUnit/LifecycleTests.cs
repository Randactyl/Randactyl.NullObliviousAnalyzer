using Xunit.v3;

[assembly: AssemblyFixture(typeof(Authoring.Tests.AssemblyState))]

namespace Authoring.Tests;

public sealed class AssemblyState : IAsyncLifetime
{
    public string Name { get; private set; } = "";

    public ValueTask InitializeAsync()
    {
        Name = "assembly";

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Name = "";

        return ValueTask.CompletedTask;
    }
}

public sealed class ClassState : IAsyncLifetime
{
    public string Name { get; private set; } = "";

    public ValueTask InitializeAsync()
    {
        Name = "class";

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Name = "";

        return ValueTask.CompletedTask;
    }
}

public sealed class LifecycleTests(ClassState classState, AssemblyState assemblyState)
    : IClassFixture<ClassState>, IAsyncLifetime
{
    private readonly MemoryStream _stream = new();

    public async ValueTask InitializeAsync()
    {
        await _stream.WriteAsync("ready"u8.ToArray(), TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync();
    }

    [Fact]
    public void First_test_sees_every_level()
    {
        Assert.Equal(5, _stream.Length);
        Assert.Equal("class", classState.Name);
        Assert.Equal("assembly", assemblyState.Name);
    }

    [Fact]
    public void Second_test_sees_every_level()
    {
        Assert.Equal(5, _stream.Length);
        Assert.Equal("class", classState.Name);
        Assert.Equal("assembly", assemblyState.Name);
    }
}
