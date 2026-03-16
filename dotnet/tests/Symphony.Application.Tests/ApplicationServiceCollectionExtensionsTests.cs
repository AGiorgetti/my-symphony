using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Symphony.Abstractions.Orchestration;
using Symphony.Application.Configuration;
using Symphony.Application.Polling;
using Symphony.Application.DependencyInjection;
using Symphony.Application.Orchestration;
using Symphony.Application.Runtime;

namespace Symphony.Application.Tests;

public class ApplicationServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSymphonyApplication_registers_application_services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IWorkflowOptionsProvider>(
            new StaticWorkflowOptionsProvider(
                new WorkflowServiceOptions(
                    new WorkflowTrackerOptions(
                        "github",
                        "https://api.github.com",
                        "token",
                        null,
                        "owner/repo",
                        null,
                        null,
                        ["Todo"],
                        ["Done"]),
                    new WorkflowPollingOptions(1_000),
                    new WorkflowWorkspaceOptions(Path.Combine(Path.GetTempPath(), "symphony-tests")),
                    new WorkflowHookOptions(
                        null,
                        null,
                        null,
                        null,
                        60_000),
                    new WorkflowAgentOptions(
                        1,
                        20,
                        300_000,
                        new Dictionary<string, int>(StringComparer.Ordinal)),
                    new WorkflowCodexOptions(
                        "codex app-server",
                        null,
                        null,
                        null,
                        3_600_000,
                        5_000,
                        300_000))));

        services.AddSymphonyApplication();

        using var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetService<ApplicationServiceMarker>());
        Assert.NotNull(serviceProvider.GetService<PollingRefreshTrigger>());
        Assert.NotNull(serviceProvider.GetService<RetryDelayPlanner>());
        Assert.IsType<ActiveSessionRegistry>(serviceProvider.GetRequiredService<IActiveSessionRegistry>());
        Assert.IsType<OrchestratorDispatchQueue>(serviceProvider.GetRequiredService<IOrchestratorDispatchQueue>());
        Assert.IsType<OrchestratorDispatchQueue>(serviceProvider.GetRequiredService<IOrchestratorDispatchStatusReader>());
        Assert.IsType<OrchestratorRuntimeService>(serviceProvider.GetRequiredService<IOrchestratorRuntimeService>());
        Assert.IsType<NoOpQueuedIssueWorker>(serviceProvider.GetRequiredService<IQueuedIssueWorker>());
        Assert.IsType<NoOpPollingIterationHandler>(serviceProvider.GetRequiredService<IPollingIterationHandler>());
        Assert.Same(TimeProvider.System, serviceProvider.GetRequiredService<TimeProvider>());
        Assert.Contains(
            serviceProvider.GetServices<IHostedService>(),
            hostedService => hostedService is DispatchWorkerBackgroundService);
        Assert.Contains(
            serviceProvider.GetServices<IHostedService>(),
            hostedService => hostedService is RetryDispatchBackgroundService);
    }

    private sealed class StaticWorkflowOptionsProvider(WorkflowServiceOptions workflowOptions) : IWorkflowOptionsProvider
    {
        public Task<WorkflowServiceOptions> GetCurrentAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(workflowOptions);
        }
    }
}
