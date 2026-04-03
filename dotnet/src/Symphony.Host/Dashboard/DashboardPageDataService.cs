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

    public string BuildExportAllLink()
    {
        return "/api/v1/export/orchestration";
    }

    public string BuildExportSessionLink(string issueIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issueIdentifier);
        return $"/api/v1/export/sessions/{Uri.EscapeDataString(issueIdentifier.Trim())}";
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

    public DashboardSessionHistorySnapshot? GetSessionHistory(DashboardPageMode mode, string issueIdentifier)
    {
        return mode.IsFake
            ? fakeDashboardPageDataSource.GetSessionHistory(issueIdentifier)
            : sessionActivityStore.GetSessionHistory(issueIdentifier);
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

    public FakeDashboardDataStatus GetFakeDataStatus()
    {
        return fakeDashboardPageDataSource.GetStatus();
    }

    public Task<FakeDashboardImportResult> ImportFakeDataAsync(
        DashboardPageMode mode,
        Stream jsonStream,
        string? sourceName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jsonStream);

        if (!mode.IsFake)
        {
            return Task.FromResult(
                new FakeDashboardImportResult(
                    false,
                    "Fake data import is only available while the dashboard is in fake mode.",
                    fakeDashboardPageDataSource.GetStatus()));
        }

        return fakeDashboardPageDataSource.ImportAsync(jsonStream, sourceName, cancellationToken);
    }
}
