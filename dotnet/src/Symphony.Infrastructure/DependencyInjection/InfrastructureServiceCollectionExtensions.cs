using Microsoft.Extensions.DependencyInjection;
using Symphony.Application.Configuration;
using Symphony.Application.Polling;
using Symphony.Abstractions.Processes;
using Symphony.Abstractions.Workflows;
using Symphony.Abstractions.Workspaces;
using Symphony.Infrastructure.Configuration;
using Symphony.Infrastructure.Processes;
using Symphony.Infrastructure.Workflows;
using Symphony.Infrastructure.Workspaces;

namespace Symphony.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddSymphonyInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<InfrastructureServiceMarker>();
        services.AddSingleton<IWorkflowOptionsResolver, WorkflowOptionsResolver>();
        services.AddSingleton<IWorkflowOptionsProvider, WorkflowOptionsProvider>();
        services.AddSingleton<IWorkflowLoader, YamlWorkflowLoader>();
        services.AddHostedService<WorkflowStartupValidationHostedService>();
        services.AddHostedService<PollingBackgroundService>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IWorkspaceManager, WorkspaceManager>();

        return services;
    }
}
