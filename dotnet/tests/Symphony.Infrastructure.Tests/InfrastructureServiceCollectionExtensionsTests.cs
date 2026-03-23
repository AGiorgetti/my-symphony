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
using Symphony.Infrastructure.Startup;
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
        services.AddSingleton<IIssueTrackerClient, StubIssueTrackerClient>();
        services.AddSymphonyApplication();

        services.AddSymphonyInfrastructure();

        using var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetService<InfrastructureServiceMarker>());
        Assert.IsType<WorkflowOptionsResolver>(serviceProvider.GetRequiredService<IWorkflowOptionsResolver>());
        Assert.IsType<WorkflowOptionsProvider>(serviceProvider.GetRequiredService<IWorkflowOptionsProvider>());
        Assert.IsType<WorkflowOptionsProvider>(serviceProvider.GetRequiredService<IWorkflowDefinitionProvider>());
        Assert.Same(
            serviceProvider.GetRequiredService<IWorkflowOptionsProvider>(),
            serviceProvider.GetRequiredService<IWorkflowDefinitionProvider>());
        Assert.IsType<WorkflowLoadStatusTracker>(serviceProvider.GetRequiredService<IWorkflowLoadStatusReader>());
        Assert.Same(
            serviceProvider.GetRequiredService<WorkflowLoadStatusTracker>(),
            serviceProvider.GetRequiredService<IWorkflowLoadStatusReader>());
        Assert.IsType<TrackerClientOptionsProvider>(serviceProvider.GetRequiredService<ITrackerClientOptionsProvider>());
        Assert.IsType<YamlWorkflowLoader>(serviceProvider.GetRequiredService<IWorkflowLoader>());
        Assert.IsType<ProcessRunner>(serviceProvider.GetRequiredService<IProcessRunner>());
        Assert.IsType<WorkspaceManager>(serviceProvider.GetRequiredService<IWorkspaceManager>());
        Assert.IsType<CodexQueuedIssueWorker>(serviceProvider.GetRequiredService<IQueuedIssueWorker>());
        Assert.IsType<OrchestratorPollingIterationHandler>(serviceProvider.GetRequiredService<IPollingIterationHandler>());
        var hostedServices = serviceProvider.GetServices<IHostedService>().ToArray();

        Assert.Contains(hostedServices, service => service is WorkflowStartupValidationHostedService);
        Assert.Contains(hostedServices, service => service is StartupTerminalWorkspaceCleanupHostedService);
        Assert.Contains(hostedServices, service => service is PollingBackgroundService);

        var startupValidationIndex = Array.FindIndex(hostedServices, static service => service is WorkflowStartupValidationHostedService);
        var startupCleanupIndex = Array.FindIndex(hostedServices, static service => service is StartupTerminalWorkspaceCleanupHostedService);
        var pollingIndex = Array.FindIndex(hostedServices, static service => service is PollingBackgroundService);

        Assert.True(startupValidationIndex >= 0);
        Assert.True(startupCleanupIndex > startupValidationIndex);
        Assert.True(pollingIndex > startupCleanupIndex);
    }

    private sealed class StubIssueTrackerClient : IIssueTrackerClient
    {
        public Task<IReadOnlyList<Domain.Issues.Issue>> FetchCandidateIssuesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Domain.Issues.Issue>>(Array.Empty<Domain.Issues.Issue>());
        }

        public Task<IReadOnlyList<Domain.Issues.Issue>> FetchIssuesByStatesAsync(
            IReadOnlyCollection<string> stateNames,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Domain.Issues.Issue>>(Array.Empty<Domain.Issues.Issue>());
        }

        public Task<IReadOnlyList<Domain.Issues.Issue>> FetchIssueStatesByIdsAsync(
            IReadOnlyCollection<string> issueIds,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Domain.Issues.Issue>>(Array.Empty<Domain.Issues.Issue>());
        }
    }
}
