using Symphony.Application.Orchestration;
using Symphony.Application.Runtime;

namespace Symphony.Host.Dashboard;

public interface IDashboardPageDataService
{
    DashboardPageMode ResolveMode(string? requestedMode);

    string BuildLink(string path, DashboardPageMode mode);

    string BuildExportAllLink();

    string BuildExportSessionLink(string issueIdentifier);

    Task<DashboardSnapshot> GetDashboardSnapshotAsync(DashboardPageMode mode, CancellationToken cancellationToken = default);

    IReadOnlyList<SessionRecord> GetAllSessions(DashboardPageMode mode);

    SessionRecord? GetSession(DashboardPageMode mode, string issueIdentifier);

    IReadOnlyList<SessionActivityEntry> GetActivities(DashboardPageMode mode, string issueIdentifier);

    Task<OrchestratorIssueSnapshot?> GetIssueSnapshotAsync(
        DashboardPageMode mode,
        string issueIdentifier,
        CancellationToken cancellationToken = default);

    Task<FollowUpActionResolutionResult> ResolveFollowUpActionAsync(
        DashboardPageMode mode,
        FollowUpActionResolutionRequest request,
        CancellationToken cancellationToken = default);

    FakeDashboardDataStatus GetFakeDataStatus();

    Task<FakeDashboardImportResult> ImportFakeDataAsync(
        DashboardPageMode mode,
        Stream jsonStream,
        string? sourceName,
        CancellationToken cancellationToken = default);
}
