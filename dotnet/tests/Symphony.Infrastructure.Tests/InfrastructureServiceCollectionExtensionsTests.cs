using Microsoft.Extensions.DependencyInjection;
using Symphony.Abstractions.Processes;
using Symphony.Infrastructure.DependencyInjection;
using Symphony.Infrastructure.Processes;

namespace Symphony.Infrastructure.Tests;

public class InfrastructureServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSymphonyInfrastructure_registers_infrastructure_marker()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSymphonyInfrastructure();

        using var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetService<InfrastructureServiceMarker>());
        Assert.IsType<ProcessRunner>(serviceProvider.GetRequiredService<IProcessRunner>());
    }
}
