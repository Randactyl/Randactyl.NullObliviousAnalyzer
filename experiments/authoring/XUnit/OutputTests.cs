using Authoring.Subject;

namespace Authoring.Tests;

public class OutputTests(ITestOutputHelper output)
{
    [Fact]
    public void Writes_diagnostic_output()
    {
        var sum = Calculator.Add(2, 3);

        output.WriteLine($"Sum was {sum}.");

        Assert.Equal(5, sum);
    }
}
