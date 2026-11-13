using Authoring.Subject;
using xRetry.v3;

namespace Authoring.Tests;

public class ControlTests
{
    private static int _attempts;

    public static bool IsWindows => OperatingSystem.IsWindows();

    public static IEnumerable<TheoryDataRow<int>> ThreeRuns()
    {
        return Enumerable.Range(1, 3).Select(run => new TheoryDataRow<int>(run));
    }

    [Fact(Skip = "Waiting for the fix")]
    public void Static_skip()
    {
        Assert.Fail("Never runs.");
    }

    [Fact(Skip = "Windows only", SkipUnless = nameof(IsWindows))]
    public void Conditional_skip()
    {
        Assert.True(OperatingSystem.IsWindows());
    }

    [Fact(Timeout = 2000)]
    public async Task Timeout_and_cancellation_token()
    {
        var service = new GreetingService(new GreetingSource());

        var greeting = await service.GreetAsync("Ada", TestContext.Current.CancellationToken);

        Assert.Equal("Hello, Ada!", greeting);
    }

    [RetryFact(3)]
    public void Retry_until_it_passes()
    {
        _attempts++;

        Assert.True(_attempts >= 3);
    }

    [Theory]
    [MemberData(nameof(ThreeRuns))]
    public void Repeat_three_times(int run)
    {
        Assert.InRange(run, 1, 3);
    }

    [Fact]
    [Trait("Category", "Fast")]
    public void Categorized_with_a_trait()
    {
        Assert.Equal(2, Calculator.Add(1, 1));
    }
}
