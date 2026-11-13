using Authoring.Subject;

namespace Authoring.Tests;

public class BasicTests
{
    [Fact]
    public void Add_returns_the_sum()
    {
        Assert.Equal(5, Calculator.Add(2, 3));
    }

    [Fact]
    public async Task GreetAsync_returns_a_greeting()
    {
        var service = new GreetingService(new GreetingSource());

        var greeting = await service.GreetAsync("Ada", TestContext.Current.CancellationToken);

        Assert.Equal("Hello, Ada!", greeting);
    }
}
