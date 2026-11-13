using Authoring.Subject;

namespace Authoring.Tests;

public class ParameterizedTests
{
    public static IEnumerable<Func<(int, int, int)>> AddCases()
    {
        yield return () => (2, 3, 5);
        yield return () => (10, -4, 6);
    }

    public static IEnumerable<Func<TestDataRow<(int, int, int)>>> NamedAddCases()
    {
        yield return () => new((1, 1, 2), DisplayName: "one plus one");
        yield return () => new((0, 0, 0), DisplayName: "zeros");
    }

    [Test]
    [Arguments(1, 2, 3)]
    [Arguments(-1, 1, 0)]
    public async Task Add_with_inline_data(int a, int b, int expected)
    {
        await Assert.That(Calculator.Add(a, b)).IsEqualTo(expected);
    }

    [Test]
    [MethodDataSource(nameof(AddCases))]
    public async Task Add_with_data_from_a_method(int a, int b, int expected)
    {
        await Assert.That(Calculator.Add(a, b)).IsEqualTo(expected);
    }

    [Test]
    [MethodDataSource(nameof(NamedAddCases))]
    public async Task Add_with_a_display_name_per_case(int a, int b, int expected)
    {
        await Assert.That(Calculator.Add(a, b)).IsEqualTo(expected);
    }
}
