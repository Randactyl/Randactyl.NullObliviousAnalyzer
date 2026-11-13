using Authoring.Subject;
using Microsoft.Extensions.DependencyInjection;

namespace Authoring.Tests;

public sealed class ServicesFixture : IDisposable
{
    private readonly ServiceProvider _provider = new ServiceCollection()
        .AddSingleton<IGreetingSource, GreetingSource>()
        .AddTransient<GreetingService>()
        .BuildServiceProvider();

    public T Get<T>()
        where T : notnull
    {
        return _provider.GetRequiredService<T>();
    }

    public void Dispose()
    {
        _provider.Dispose();
    }
}

public class DependencyInjectionTests(ServicesFixture services) : IClassFixture<ServicesFixture>
{
    [Fact]
    public void Service_comes_from_the_container()
    {
        var service = services.Get<GreetingService>();

        Assert.Equal("Hello, Ada!", service.Greet("Ada"));
    }
}
