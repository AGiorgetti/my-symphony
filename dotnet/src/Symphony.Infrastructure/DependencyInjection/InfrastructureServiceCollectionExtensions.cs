using Microsoft.Extensions.DependencyInjection;
using Symphony.Abstractions.Processes;
using Symphony.Infrastructure.Processes;

namespace Symphony.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddSymphonyInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<InfrastructureServiceMarker>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();

        return services;
    }
}
