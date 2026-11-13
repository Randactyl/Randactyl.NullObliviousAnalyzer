using Authoring.Subject;

namespace Authoring.Tests;

public class OutputTests
{
    [Test]
    public async Task Writes_diagnostic_output()
    {
        var sum = Calculator.Add(2, 3);

        TestContext.Current!.Output.WriteLine($"Sum was {sum}.");

        await Assert.That(sum).IsEqualTo(5);
    }
}
