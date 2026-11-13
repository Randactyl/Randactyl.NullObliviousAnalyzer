using Authoring.Subject;

namespace Authoring.Tests;

public sealed class ExpensiveFixture : IAsyncLifetime
{
    public ExpensiveResource Resource { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Resource = await ExpensiveResource.CreateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Resource.DisposeAsync();
    }
}

[CollectionDefinition("Expensive")]
public class ExpensiveCollection : ICollectionFixture<ExpensiveFixture>
{
}

[Collection("Expensive")]
public class FirstFixtureTests(ExpensiveFixture fixture)
{
    [Fact]
    public void Resource_was_created_once()
    {
        Assert.Equal(1, ExpensiveResource.Created);
        Assert.False(fixture.Resource.IsDisposed);
    }
}

[Collection("Expensive")]
public class SecondFixtureTests(ExpensiveFixture fixture)
{
    [Fact]
    public void Resource_was_created_once()
    {
        Assert.Equal(1, ExpensiveResource.Created);
        Assert.False(fixture.Resource.IsDisposed);
    }
}
