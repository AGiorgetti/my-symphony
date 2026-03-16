using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Symphony.Application.Polling;

namespace Symphony.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddSymphonyApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ApplicationServiceMarker>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPollingIterationHandler, NoOpPollingIterationHandler>();

        return services;
    }
}
