using Microsoft.Extensions.DependencyInjection;
using Symphony.Abstractions.Workflows;
using Symphony.Infrastructure.Workflows;

namespace Symphony.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddSymphonyInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<InfrastructureServiceMarker>();
        services.AddSingleton<IWorkflowLoader, YamlWorkflowLoader>();
        services.AddHostedService<WorkflowStartupValidationHostedService>();

        return services;
    }
}
