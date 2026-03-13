using Microsoft.Extensions.DependencyInjection;
using Symphony.Application.DependencyInjection;

namespace Symphony.Application.Tests;

public class ApplicationServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSymphonyApplication_registers_application_marker()
    {
        var services = new ServiceCollection();

        services.AddSymphonyApplication();

        using var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetService<ApplicationServiceMarker>());
    }
}
