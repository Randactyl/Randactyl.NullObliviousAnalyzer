namespace Authoring.Subject;

public interface IGreetingSource
{
    string Template { get; }
}

public sealed class GreetingSource : IGreetingSource
{
    public string Template => "Hello, {0}!";
}

public sealed class GreetingService(IGreetingSource source)
{
    public string Greet(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Name is required.");
        }

        return string.Format(source.Template, name);
    }

    public async Task<string> GreetAsync(string name, CancellationToken cancellationToken = default)
    {
        await Task.Delay(10, cancellationToken);

        return Greet(name);
    }

    public string? FindNickname(string name)
    {
        return name == "Robert" ? "Bob" : null;
    }
}
