using Symphony.Application.Orchestration;
using Symphony.Application.Runtime;

namespace Symphony.Host.Dashboard;

public sealed class DashboardPageDataService(
    IDashboardPageModeResolver modeResolver,
    IDashboardStateService dashboardStateService,
    IOrchestratorRuntimeService orchestratorRuntimeService,
    ISessionActivityStore sessionActivityStore,
    FollowUpActionResolutionService followUpActionResolutionService,
    FakeDashboardPageDataSource fakeDashboardPageDataSource) : IDashboardPageDataService
{
    public DashboardPageMode ResolveMode(string? requestedMode)
    {
        return modeResolver.Resolve(requestedMode);
    }

    public string BuildLink(string path, DashboardPageMode mode)
    {
        return DashboardPageLinks.WithMode(path, mode);
    }

    public Task<DashboardSnapshot> GetDashboardSnapshotAsync(DashboardPageMode mode, CancellationToken cancellationToken = default)
    {
        return mode.IsFake
            ? fakeDashboardPageDataSource.GetDashboardSnapshotAsync(cancellationToken)
            : dashboardStateService.GetSnapshotAsync(cancellationToken);
    }

    public IReadOnlyList<SessionRecord> GetAllSessions(DashboardPageMode mode)
    {
        return mode.IsFake
            ? fakeDashboardPageDataSource.GetAllSessions()
            : sessionActivityStore.GetAllSessions();
    }

    public SessionRecord? GetSession(DashboardPageMode mode, string issueIdentifier)
    {
        return mode.IsFake
            ? fakeDashboardPageDataSource.GetSession(issueIdentifier)
            : sessionActivityStore.GetSession(issueIdentifier);
    }

    public IReadOnlyList<SessionActivityEntry> GetActivities(DashboardPageMode mode, string issueIdentifier)
    {
        return mode.IsFake
            ? fakeDashboardPageDataSource.GetActivities(issueIdentifier)
            : sessionActivityStore.GetActivities(issueIdentifier);
    }

    public Task<OrchestratorIssueSnapshot?> GetIssueSnapshotAsync(
        DashboardPageMode mode,
        string issueIdentifier,
        CancellationToken cancellationToken = default)
    {
        return mode.IsFake
            ? fakeDashboardPageDataSource.GetIssueSnapshotAsync(issueIdentifier, cancellationToken)
            : orchestratorRuntimeService.GetIssueSnapshotAsync(issueIdentifier, cancellationToken);
    }

    public Task<FollowUpActionResolutionResult> ResolveFollowUpActionAsync(
        DashboardPageMode mode,
        FollowUpActionResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        return mode.IsFake
            ? fakeDashboardPageDataSource.ResolveFollowUpActionAsync(request, cancellationToken)
            : followUpActionResolutionService.ResolveAsync(request, cancellationToken);
    }
}
