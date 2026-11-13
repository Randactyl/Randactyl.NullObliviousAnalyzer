using Authoring.Subject;

namespace Authoring.Tests;

public class ParameterizedTests
{
    public static IEnumerable<TheoryDataRow<int, int, int>> AddCases()
    {
        yield return new(2, 3, 5);
        yield return new(10, -4, 6);
    }

    public static IEnumerable<TheoryDataRow<int, int, int>> NamedAddCases()
    {
        yield return new(1, 1, 2) { TestDisplayName = "one plus one" };
        yield return new(0, 0, 0) { TestDisplayName = "zeros" };
    }

    [Theory]
    [InlineData(1, 2, 3)]
    [InlineData(-1, 1, 0)]
    public void Add_with_inline_data(int a, int b, int expected)
    {
        Assert.Equal(expected, Calculator.Add(a, b));
    }

    [Theory]
    [MemberData(nameof(AddCases))]
    public void Add_with_data_from_a_method(int a, int b, int expected)
    {
        Assert.Equal(expected, Calculator.Add(a, b));
    }

    [Theory]
    [MemberData(nameof(NamedAddCases))]
    public void Add_with_a_display_name_per_case(int a, int b, int expected)
    {
        Assert.Equal(expected, Calculator.Add(a, b));
    }
}
