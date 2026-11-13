using Authoring.Subject;
using TUnit.Core.Interfaces;

[assembly: ParallelLimiter<Authoring.Tests.OverallLimit>]

namespace Authoring.Tests;

public sealed class OverallLimit : IParallelLimit
{
    public int Limit => 4;
}

[NotInParallel("Sequential")]
public class SequentialFirstTests
{
    [Test]
    public async Task Runs_alone_in_its_group()
    {
        using var _ = ConcurrencyProbe.Sequential.Enter();

        await Task.Delay(100);

        await Assert.That(ConcurrencyProbe.Sequential.Current).IsEqualTo(1);
    }
}

[NotInParallel("Sequential")]
public class SequentialSecondTests
{
    [Test]
    public async Task Runs_alone_in_its_group()
    {
        using var _ = ConcurrencyProbe.Sequential.Enter();

        await Task.Delay(100);

        await Assert.That(ConcurrencyProbe.Sequential.Current).IsEqualTo(1);
    }
}
