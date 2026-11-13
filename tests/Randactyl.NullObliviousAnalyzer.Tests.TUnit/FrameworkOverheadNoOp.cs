using System.Threading.Tasks;
using TUnit.Core;

namespace Randactyl.NullObliviousAnalyzer.Tests;

/// <summary>Measurement baseline only: compiled when Configuration is NoOp (see eng/compare-test-frameworks.ps1). Not one of the 105 tests.</summary>
public class FrameworkOverheadNoOp
{
    [Test]
    public Task NoOp() => Task.CompletedTask;
}
