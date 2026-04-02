using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Symphony.Abstractions.Trackers;
using Symphony.Application.Configuration;
using Symphony.Application.Orchestration;
using Symphony.Application.Polling;
using Symphony.Application.Runtime;
using Symphony.Domain.Issues;
using Symphony.Host.Configuration;
using Symphony.Host.Dashboard;

namespace Symphony.Host.IntegrationTests;

internal static class DashboardPageDataTestServices
{
    public static IServiceCollection AddDashboardPageDataServices(
        this IServiceCollection services,
        IDashboardStateService dashboardStateService,
        IOrchestratorRuntimeService? runtimeService = null,
        ISessionActivityStore? sessionActivityStore = null,
        FollowUpActionResolutionService? followUpActionResolutionService = null,
        Action<DashboardUiOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dashboardStateService);

        services.AddOptions();
        services.Configure<DashboardUiOptions>(options => configureOptions?.Invoke(options));
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);

        var effectiveRuntimeService = runtimeService ?? new StubRuntimeService();
        var effectiveSessionStore = sessionActivityStore ?? new SessionActivityStore(NullLogger<SessionActivityStore>.Instance);
        var effectiveResolutionService = followUpActionResolutionService ?? CreateFollowUpActionResolutionService();

        services.AddSingleton(dashboardStateService);
        services.AddSingleton<IDashboardStateService>(dashboardStateService);
        services.AddSingleton(effectiveRuntimeService);
        services.AddSingleton<IOrchestratorRuntimeService>(effectiveRuntimeService);
        services.AddSingleton(effectiveSessionStore);
        services.AddSingleton<ISessionActivityStore>(effectiveSessionStore);
        services.AddSingleton(effectiveResolutionService);
        services.AddSingleton<IDashboardPageModeResolver, DashboardPageModeResolver>();
        services.AddSingleton<FakeDashboardPageDataSource>();
        services.AddSingleton<IDashboardPageDataService, DashboardPageDataService>();

        return services;
    }

    public static FollowUpActionResolutionService CreateFollowUpActionResolutionService()
    {
        var workflowOptionsProvider = new StaticWorkflowOptionsProvider(
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
                    new Dictionary<string, int>(StringComparer.Ordinal),
                    false,
                    "exec:agent"),
                new WorkflowCodexOptions(
                    "codex app-server",
                    null,
                    null,
                    null,
                    3_600_000,
                    5_000,
                    300_000)));
        var queue = new OrchestratorDispatchQueue(
            workflowOptionsProvider,
            new RetryDelayPlanner(() => 1d),
            TimeProvider.System,
            NullLogger<OrchestratorDispatchQueue>.Instance);

        return new FollowUpActionResolutionService(
            new FollowUpActionRegistry(TimeProvider.System),
            queue,
            new StubIssueTrackerClient(),
            workflowOptionsProvider);
    }

    private sealed class StubRuntimeService : IOrchestratorRuntimeService
    {
        public Task<OrchestratorStateSnapshot> GetStateSnapshotAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new OrchestratorStateSnapshot(
                    DateTimeOffset.UtcNow,
                    Array.Empty<RunningIssueSnapshot>(),
                    Array.Empty<Symphony.Abstractions.Orchestration.RetryDispatchSnapshot>(),
                    new CodexTotalsSnapshot(0, 0, 0, 0d),
                    RateLimits: null));
        }

        public Task<OrchestratorIssueSnapshot?> GetIssueSnapshotAsync(string issueIdentifier, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<OrchestratorIssueSnapshot?>(null);
        }

        public PollingRefreshReceipt RequestRefresh()
        {
            return new PollingRefreshReceipt(true, false, DateTimeOffset.UtcNow, ["poll", "reconcile"]);
        }
    }

    private sealed class StaticWorkflowOptionsProvider(WorkflowServiceOptions workflowOptions) : IWorkflowOptionsProvider
    {
        public Task<WorkflowServiceOptions> GetCurrentAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(workflowOptions);
        }
    }

    private sealed class StubIssueTrackerClient : IIssueTrackerClient
    {
        public Task<IReadOnlyList<Issue>> FetchCandidateIssuesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Issue>>(Array.Empty<Issue>());
        }

        public Task<IReadOnlyList<Issue>> FetchIssuesByStatesAsync(
            IReadOnlyCollection<string> stateNames,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Issue>>(Array.Empty<Issue>());
        }

        public Task<IReadOnlyList<Issue>> FetchIssueStatesByIdsAsync(
            IReadOnlyCollection<string> issueIds,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Issue>>(Array.Empty<Issue>());
        }
    }
}
