namespace Authoring.Subject;

public static class Calculator
{
    public static int Add(int a, int b)
    {
        return a + b;
    }

    public static int Divide(int a, int b)
    {
        return a / b;
    }

    public static double Average(params double[] values)
    {
        return values.Sum() / values.Length;
    }

    public static IReadOnlyList<int> Evens(int upTo)
    {
        return Enumerable.Range(0, upTo + 1).Where(n => n % 2 == 0).ToList();
    }
}
