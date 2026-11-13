using System.Threading.Tasks;
using Xunit;

namespace Randactyl.NullObliviousAnalyzer.Tests;

/// <summary>Measurement baseline only: compiled when Configuration is NoOp (see eng/compare-test-frameworks.ps1). Not one of the 105 tests.</summary>
public class FrameworkOverheadNoOp
{
    [Fact]
    public Task NoOp() => Task.CompletedTask;
}
