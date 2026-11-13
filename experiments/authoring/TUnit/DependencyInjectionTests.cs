using Authoring.Subject;
using Microsoft.Extensions.DependencyInjection;

namespace Authoring.Tests;

public sealed class ServicesAttribute : DependencyInjectionDataSourceAttribute<IServiceScope>
{
    private static readonly ServiceProvider Provider = new ServiceCollection()
        .AddSingleton<IGreetingSource, GreetingSource>()
        .AddTransient<GreetingService>()
        .BuildServiceProvider();

    public override IServiceScope CreateScope(DataGeneratorMetadata dataGeneratorMetadata)
    {
        return Provider.CreateScope();
    }

    public override object? Create(IServiceScope scope, Type type)
    {
        return scope.ServiceProvider.GetService(type);
    }
}

[Services]
public class DependencyInjectionTests(GreetingService service)
{
    [Test]
    public async Task Service_comes_from_the_container()
    {
        await Assert.That(service.Greet("Ada")).IsEqualTo("Hello, Ada!");
    }
}
