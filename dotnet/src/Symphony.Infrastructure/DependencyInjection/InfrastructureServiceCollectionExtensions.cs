using Microsoft.Extensions.DependencyInjection;
using Symphony.Application.Configuration;
using Symphony.Abstractions.Workflows;
using Symphony.Infrastructure.Configuration;
using Symphony.Infrastructure.Workflows;

namespace Symphony.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddSymphonyInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<InfrastructureServiceMarker>();
        services.AddSingleton<IWorkflowOptionsResolver, WorkflowOptionsResolver>();
        services.AddSingleton<IWorkflowLoader, YamlWorkflowLoader>();
        services.AddHostedService<WorkflowStartupValidationHostedService>();

        return services;
    }
}
