using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Symphony.Application.Configuration;
using Symphony.Application.DependencyInjection;
using Symphony.Application.Polling;
using Symphony.Abstractions.Processes;
using Symphony.Abstractions.Trackers;
using Symphony.Abstractions.Workflows;
using Symphony.Abstractions.Workspaces;
using Symphony.Application.Orchestration;
using Symphony.Infrastructure.Configuration;
using Symphony.Infrastructure.Orchestration;
using Symphony.Infrastructure.DependencyInjection;
using Symphony.Infrastructure.Processes;
using Symphony.Infrastructure.Workflows;
using Symphony.Infrastructure.Workspaces;

namespace Symphony.Infrastructure.Tests;

public class InfrastructureServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSymphonyInfrastructure_registers_infrastructure_services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSymphonyApplication();

        services.AddSymphonyInfrastructure();

        using var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetService<InfrastructureServiceMarker>());
        Assert.IsType<WorkflowOptionsResolver>(serviceProvider.GetRequiredService<IWorkflowOptionsResolver>());
        Assert.IsType<WorkflowOptionsProvider>(serviceProvider.GetRequiredService<IWorkflowOptionsProvider>());
        Assert.IsType<TrackerClientOptionsProvider>(serviceProvider.GetRequiredService<ITrackerClientOptionsProvider>());
        Assert.IsType<YamlWorkflowLoader>(serviceProvider.GetRequiredService<IWorkflowLoader>());
        Assert.IsType<ProcessRunner>(serviceProvider.GetRequiredService<IProcessRunner>());
        Assert.IsType<WorkspaceManager>(serviceProvider.GetRequiredService<IWorkspaceManager>());
        Assert.IsType<CodexQueuedIssueWorker>(serviceProvider.GetRequiredService<IQueuedIssueWorker>());
        var hostedServices = serviceProvider.GetServices<IHostedService>().ToArray();

        Assert.Contains(hostedServices, service => service is WorkflowStartupValidationHostedService);
        Assert.Contains(hostedServices, service => service is PollingBackgroundService);
    }
}
