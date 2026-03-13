using Microsoft.Extensions.DependencyInjection;
using Symphony.Infrastructure.DependencyInjection;

namespace Symphony.Infrastructure.Tests;

public class InfrastructureServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSymphonyInfrastructure_registers_infrastructure_marker()
    {
        var services = new ServiceCollection();

        services.AddSymphonyInfrastructure();

        using var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetService<InfrastructureServiceMarker>());
    }
}
