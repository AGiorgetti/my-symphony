using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Symphony.Abstractions.Workflows;
using Symphony.Infrastructure.DependencyInjection;
using Symphony.Infrastructure.Workflows;

namespace Symphony.Infrastructure.Tests;

public class InfrastructureServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSymphonyInfrastructure_registers_infrastructure_services()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSymphonyInfrastructure();

        using var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetService<InfrastructureServiceMarker>());
        Assert.IsType<YamlWorkflowLoader>(serviceProvider.GetRequiredService<IWorkflowLoader>());

        var hostedServices = serviceProvider.GetServices<IHostedService>().ToArray();

        Assert.Contains(hostedServices, service => service is WorkflowStartupValidationHostedService);
    }
}
