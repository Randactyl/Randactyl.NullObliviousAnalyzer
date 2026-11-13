namespace Authoring.Tests;

public sealed class WindowsOnlyAttribute() : SkipAttribute("Windows only.")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context)
    {
        return Task.FromResult(!OperatingSystem.IsWindows());
    }
}

public class CustomAttributeTests
{
    [Test]
    [WindowsOnly]
    public async Task Runs_only_on_windows()
    {
        await Assert.That(OperatingSystem.IsWindows()).IsTrue();
    }
}
