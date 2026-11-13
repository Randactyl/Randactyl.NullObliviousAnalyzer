using Authoring.Subject;

namespace Authoring.Tests;

public class AssertionTests
{
    private readonly GreetingService _service = new(new GreetingSource());

    [Test]
    public async Task Equality()
    {
        await Assert.That(Calculator.Add(2, 2)).IsEqualTo(4);
        await Assert.That(Calculator.Add(2, 2)).IsNotEqualTo(5);
    }

    [Test]
    public async Task Null_and_not_null()
    {
        await Assert.That(_service.FindNickname("Ada")).IsNull();
        await Assert.That(_service.FindNickname("Robert")).IsNotNull();
    }

    [Test]
    public async Task Collection_contents()
    {
        var evens = Calculator.Evens(10);

        await Assert.That(evens).IsEquivalentTo(new[] { 0, 2, 4, 6, 8, 10 });
        await Assert.That(evens).Contains(4);
        await Assert.That(evens).DoesNotContain(5);
    }

    [Test]
    public async Task String_contains()
    {
        await Assert.That(_service.Greet("Ada")).Contains("Ada");
    }

    [Test]
    public async Task Numeric_tolerance()
    {
        await Assert.That(Calculator.Average(0.1, 0.2, 0.3)).IsEqualTo(0.2).Within(1e-9);
    }

    [Test]
    public async Task Exception_type_and_message()
    {
        await Assert.That(() => Calculator.Divide(1, 0))
            .Throws<DivideByZeroException>()
            .WithMessage("Attempted to divide by zero.");
    }

    [Test]
    public async Task Async_exception_type_and_message()
    {
        await Assert.That(async () => await _service.GreetAsync(""))
            .Throws<InvalidOperationException>()
            .WithMessage("Name is required.");
    }

    [Test]
    public async Task Multiple_assertions_are_all_reported()
    {
        using (Assert.Multiple())
        {
            await Assert.That(Calculator.Add(2, 3)).IsEqualTo(5);
            await Assert.That(_service.Greet("Ada")).IsEqualTo("Hello, Ada!");
            await Assert.That(_service.FindNickname("Ada")).IsNull();
        }
    }
}
