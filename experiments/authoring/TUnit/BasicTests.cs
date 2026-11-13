using Authoring.Subject;

namespace Authoring.Tests;

public class BasicTests
{
    [Test]
    public async Task Add_returns_the_sum()
    {
        await Assert.That(Calculator.Add(2, 3)).IsEqualTo(5);
    }

    [Test]
    public async Task GreetAsync_returns_a_greeting()
    {
        var service = new GreetingService(new GreetingSource());

        var greeting = await service.GreetAsync("Ada");

        await Assert.That(greeting).IsEqualTo("Hello, Ada!");
    }
}
