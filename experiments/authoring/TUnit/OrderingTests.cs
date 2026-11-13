namespace Authoring.Tests;

public class OrderingTests
{
    private static bool _step1Ran;

    [Test]
    public void Step1_runs_first()
    {
        _step1Ran = true;
    }

    [Test]
    [DependsOn(nameof(Step1_runs_first))]
    public async Task Step2_runs_after_step1()
    {
        await Assert.That(_step1Ran).IsTrue();
    }
}
