using Authoring.Subject;

namespace Authoring.Tests;

public class AssertionTests
{
    private readonly GreetingService _service = new(new GreetingSource());

    [Fact]
    public void Equality()
    {
        Assert.Equal(4, Calculator.Add(2, 2));
        Assert.NotEqual(5, Calculator.Add(2, 2));
    }

    [Fact]
    public void Null_and_not_null()
    {
        Assert.Null(_service.FindNickname("Ada"));
        Assert.NotNull(_service.FindNickname("Robert"));
    }

    [Fact]
    public void Collection_contents()
    {
        var evens = Calculator.Evens(10);

        Assert.Equal(new[] { 0, 2, 4, 6, 8, 10 }, evens);
        Assert.Contains(4, evens);
        Assert.DoesNotContain(5, evens);
    }

    [Fact]
    public void String_contains()
    {
        Assert.Contains("Ada", _service.Greet("Ada"));
    }

    [Fact]
    public void Numeric_tolerance()
    {
        Assert.Equal(0.2, Calculator.Average(0.1, 0.2, 0.3), 1e-9);
    }

    [Fact]
    public void Exception_type_and_message()
    {
        var exception = Assert.Throws<DivideByZeroException>(() => Calculator.Divide(1, 0));

        Assert.Equal("Attempted to divide by zero.", exception.Message);
    }

    [Fact]
    public async Task Async_exception_type_and_message()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.GreetAsync("", TestContext.Current.CancellationToken));

        Assert.Equal("Name is required.", exception.Message);
    }

    [Fact]
    public void Multiple_assertions_are_all_reported()
    {
        Assert.Multiple(
            () => Assert.Equal(5, Calculator.Add(2, 3)),
            () => Assert.Equal("Hello, Ada!", _service.Greet("Ada")),
            () => Assert.Null(_service.FindNickname("Ada")));
    }
}
