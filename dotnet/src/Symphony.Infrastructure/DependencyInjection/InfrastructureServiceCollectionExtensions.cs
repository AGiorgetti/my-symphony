using Microsoft.Extensions.DependencyInjection;
using Symphony.Application.Configuration;
using Symphony.Application.Polling;
using Symphony.Abstractions.Processes;
using Symphony.Abstractions.Workflows;
using Symphony.Abstractions.Workspaces;
using Symphony.Abstractions.Trackers;
using Symphony.Application.Orchestration;
using Symphony.Infrastructure.Configuration;
using Symphony.Infrastructure.Codex;
using Symphony.Infrastructure.Orchestration;
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
        services.AddSingleton<WorkflowLoadStatusTracker>();
        services.AddSingleton<IWorkflowLoadStatusReader>(serviceProvider => serviceProvider.GetRequiredService<WorkflowLoadStatusTracker>());
        services.AddSingleton<WorkflowOptionsProvider>();
        services.AddSingleton<IWorkflowOptionsProvider>(serviceProvider => serviceProvider.GetRequiredService<WorkflowOptionsProvider>());
        services.AddSingleton<IWorkflowDefinitionProvider>(serviceProvider => serviceProvider.GetRequiredService<WorkflowOptionsProvider>());
        services.AddSingleton<ITrackerClientOptionsProvider, TrackerClientOptionsProvider>();
        services.AddSingleton<IWorkflowLoader, YamlWorkflowLoader>();
        services.AddHostedService<WorkflowStartupValidationHostedService>();
        services.AddHostedService<PollingBackgroundService>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IWorkspaceManager, WorkspaceManager>();
        services.AddSingleton<WorkflowPromptRenderer>();
        services.AddSingleton<ICodexProcessSessionFactory, ProcessCodexProcessSessionFactory>();
        services.AddSingleton<CodexAppServerClient>();
        services.AddSingleton<IQueuedIssueWorker, CodexQueuedIssueWorker>();

        return services;
    }
}
