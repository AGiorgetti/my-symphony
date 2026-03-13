using Microsoft.Extensions.DependencyInjection;

namespace Symphony.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddSymphonyApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ApplicationServiceMarker>();

        return services;
    }
}
