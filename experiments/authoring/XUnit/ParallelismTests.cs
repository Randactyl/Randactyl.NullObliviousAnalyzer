using Authoring.Subject;
using Xunit.v3;

[assembly: Parallelization(MaxThreads = 4)]

namespace Authoring.Tests;

[Collection("Sequential")]
public class SequentialFirstTests
{
    [Fact]
    public async Task Runs_alone_in_its_group()
    {
        using var _ = ConcurrencyProbe.Sequential.Enter();

        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal(1, ConcurrencyProbe.Sequential.Current);
    }
}

[Collection("Sequential")]
public class SequentialSecondTests
{
    [Fact]
    public async Task Runs_alone_in_its_group()
    {
        using var _ = ConcurrencyProbe.Sequential.Enter();

        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal(1, ConcurrencyProbe.Sequential.Current);
    }
}
