using Microsoft.Extensions.DependencyInjection;
using Symphony.Application.Polling;
using Symphony.Application.DependencyInjection;

namespace Symphony.Application.Tests;

public class ApplicationServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSymphonyApplication_registers_application_services()
    {
        var services = new ServiceCollection();

        services.AddSymphonyApplication();

        using var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetService<ApplicationServiceMarker>());
        Assert.IsType<NoOpPollingIterationHandler>(serviceProvider.GetRequiredService<IPollingIterationHandler>());
        Assert.Same(TimeProvider.System, serviceProvider.GetRequiredService<TimeProvider>());
    }
}
