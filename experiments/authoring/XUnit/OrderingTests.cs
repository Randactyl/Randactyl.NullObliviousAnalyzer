using Xunit.Sdk;
using Xunit.v3;

namespace Authoring.Tests;

public class AlphabeticalOrderer : ITestMethodOrderer
{
    public IReadOnlyCollection<TTestMethod?> OrderTestMethods<TTestMethod>(IReadOnlyCollection<TTestMethod?> testMethods)
        where TTestMethod : ITestMethod
    {
        return [.. testMethods.OrderBy(testMethod => testMethod?.MethodName)];
    }
}

[TestMethodOrderer(typeof(AlphabeticalOrderer))]
public class OrderingTests
{
    private static bool _step1Ran;

    [Fact]
    public void Step1_runs_first()
    {
        _step1Ran = true;
    }

    [Fact]
    public void Step2_runs_after_step1()
    {
        Assert.True(_step1Ran);
    }
}
