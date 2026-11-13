using System.Runtime.CompilerServices;

namespace Authoring.Tests;

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Windows only.";
        }
    }
}

public class CustomAttributeTests
{
    [WindowsFact]
    public void Runs_only_on_windows()
    {
        Assert.True(OperatingSystem.IsWindows());
    }
}
