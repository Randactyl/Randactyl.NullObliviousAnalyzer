using Authoring.Subject;
using TUnit.Core.Interfaces;

namespace Authoring.Tests;

public sealed class ExpensiveFixture : IAsyncInitializer, IAsyncDisposable
{
    public ExpensiveResource Resource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Resource = await ExpensiveResource.CreateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Resource.DisposeAsync();
    }
}

[ClassDataSource<ExpensiveFixture>(Shared = SharedType.PerTestSession)]
public class FirstFixtureTests(ExpensiveFixture fixture)
{
    [Test]
    public async Task Resource_was_created_once()
    {
        await Assert.That(ExpensiveResource.Created).IsEqualTo(1);
        await Assert.That(fixture.Resource.IsDisposed).IsFalse();
    }
}

[ClassDataSource<ExpensiveFixture>(Shared = SharedType.PerTestSession)]
public class SecondFixtureTests(ExpensiveFixture fixture)
{
    [Test]
    public async Task Resource_was_created_once()
    {
        await Assert.That(ExpensiveResource.Created).IsEqualTo(1);
        await Assert.That(fixture.Resource.IsDisposed).IsFalse();
    }
}
