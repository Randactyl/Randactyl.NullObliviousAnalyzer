using Authoring.Subject;

namespace Authoring.Tests;

public class ControlTests
{
    private static int _attempts;

    [Test]
    [Skip("Waiting for the fix")]
    public void Static_skip()
    {
        throw new InvalidOperationException("Never runs.");
    }

    [Test]
    public async Task Conditional_skip()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("Windows only");
        }

        await Assert.That(OperatingSystem.IsWindows()).IsTrue();
    }

    [Test]
    [Timeout(2000)]
    public async Task Timeout_and_cancellation_token(CancellationToken cancellationToken)
    {
        var service = new GreetingService(new GreetingSource());

        var greeting = await service.GreetAsync("Ada", cancellationToken);

        await Assert.That(greeting).IsEqualTo("Hello, Ada!");
    }

    [Test]
    [Retry(3)]
    public async Task Retry_until_it_passes()
    {
        _attempts++;

        await Assert.That(_attempts).IsGreaterThanOrEqualTo(3);
    }

    [Test]
    [Repeat(2)]
    public async Task Repeat_three_times()
    {
        await Assert.That(Calculator.Add(1, 1)).IsEqualTo(2);
    }

    [Test]
    [Category("Fast")]
    public async Task Categorized_with_a_category()
    {
        await Assert.That(Calculator.Add(1, 1)).IsEqualTo(2);
    }
}
