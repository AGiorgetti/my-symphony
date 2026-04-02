using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Symphony.Abstractions.Orchestration;
using Symphony.Application.Runtime;
using Symphony.Host.Configuration;

namespace Symphony.Host.Dashboard;

public sealed class DashboardDataExportService(
    IHostEnvironment hostEnvironment,
    IDashboardStateService dashboardStateService,
    IOrchestratorRuntimeService orchestratorRuntimeService,
    ISessionActivityStore sessionActivityStore,
    IOrchestratorControlStatusReader orchestratorControlStatusReader,
    IOptions<DashboardUiOptions> dashboardUiOptions) : IDashboardDataExportService
{
    public async Task<DashboardDataExportEnvelope?> ExportSingleSessionAsync(
        string issueIdentifier,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issueIdentifier);

        var normalizedIssueIdentifier = issueIdentifier.Trim();
        var sessionHistory = sessionActivityStore.GetSessionHistory(normalizedIssueIdentifier);
        if (sessionHistory is null)
        {
            return null;
        }

        var dashboardSnapshot = await dashboardStateService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var issueSnapshot = await orchestratorRuntimeService.GetIssueSnapshotAsync(normalizedIssueIdentifier, cancellationToken).ConfigureAwait(false);
        var session = sessionHistory.Session;

        var activeSession = dashboardSnapshot.ActiveSessions
            .FirstOrDefault(candidate => string.Equals(candidate.IssueIdentifier, normalizedIssueIdentifier, StringComparison.OrdinalIgnoreCase));
        var retryEntry = dashboardSnapshot.RetryQueue
            .FirstOrDefault(candidate => string.Equals(candidate.IssueIdentifier, normalizedIssueIdentifier, StringComparison.OrdinalIgnoreCase));
        var recentAttempt = dashboardSnapshot.RecentAttempts
            .Where(candidate => string.Equals(candidate.IssueIdentifier, normalizedIssueIdentifier, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.CompletedAt)
            .FirstOrDefault();
        var blockedSession = (dashboardSnapshot.BlockedSessions ?? [])
            .FirstOrDefault(candidate => string.Equals(candidate.IssueIdentifier, normalizedIssueIdentifier, StringComparison.OrdinalIgnoreCase));
        var followUpActions = (issueSnapshot?.FollowUpActions ?? dashboardSnapshot.FollowUpActions ?? [])
            .Where(candidate => string.Equals(candidate.IssueIdentifier, normalizedIssueIdentifier, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var sessionMetadata = CreateSessionMetadataSnapshot(session, activeSession, issueSnapshot, dashboardSnapshot, sessionHistory.Metadata);

        return new DashboardDataExportEnvelope(
            DashboardDataExportSchema.CurrentVersion,
            DateTimeOffset.UtcNow,
            DashboardDataExportSchema.SingleSessionKind,
            CreateSource(hostEnvironment),
            new DashboardDataSessionExport(
                sessionHistory.Session,
                sessionHistory.Activities,
                sessionHistory,
                issueSnapshot,
                activeSession,
                retryEntry,
                recentAttempt,
                blockedSession,
                followUpActions,
                sessionMetadata),
            Bundle: null);
    }

    public async Task<DashboardDataExportEnvelope> ExportFullBundleAsync(CancellationToken cancellationToken = default)
    {
        var dashboardSnapshot = await dashboardStateService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var runtimeState = await orchestratorRuntimeService.GetStateSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var sessions = sessionActivityStore.GetAllSessions();
        var issueIdentifiers = new HashSet<string>(
            sessions.Select(history => history.IssueIdentifier),
            StringComparer.OrdinalIgnoreCase);
        issueIdentifiers.UnionWith(runtimeState.Running.Select(issue => issue.IssueIdentifier));
        issueIdentifiers.UnionWith(runtimeState.Retrying.Select(issue => issue.IssueIdentifier));
        issueIdentifiers.UnionWith((runtimeState.Blocked ?? []).Select(issue => issue.IssueIdentifier));
        issueIdentifiers.UnionWith((runtimeState.FollowUpActions ?? []).Select(action => action.IssueIdentifier));

        var issueSnapshots = new List<OrchestratorIssueSnapshot>(issueIdentifiers.Count);
        foreach (var issueIdentifier in issueIdentifiers.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            var snapshot = await orchestratorRuntimeService.GetIssueSnapshotAsync(issueIdentifier, cancellationToken).ConfigureAwait(false);
            if (snapshot is not null)
            {
                issueSnapshots.Add(snapshot);
            }
        }
        var issueSnapshotMap = issueSnapshots.ToDictionary(snapshot => snapshot.IssueIdentifier, StringComparer.OrdinalIgnoreCase);
        var allSessions = sessionActivityStore.GetAllSessionHistories()
            .Select(history => EnsureHistoryMetadata(
                history,
                dashboardSnapshot,
                issueSnapshotMap.TryGetValue(history.Session.IssueIdentifier, out var issueSnapshot) ? issueSnapshot : null))
            .ToArray();

        var options = dashboardUiOptions.Value;
        return new DashboardDataExportEnvelope(
            DashboardDataExportSchema.CurrentVersion,
            DateTimeOffset.UtcNow,
            DashboardDataExportSchema.FullBundleKind,
            CreateSource(hostEnvironment),
            SingleSession: null,
            new DashboardDataBundleExport(
                dashboardSnapshot,
                runtimeState,
                orchestratorControlStatusReader.GetSnapshot(),
                new DashboardUiOptionsSnapshot(
                    options.DebugMode,
                    options.TrackAgentMessageDeltas,
                    options.EnableFakeDataMode,
                    options.FakeDataJsonPath),
                allSessions,
                issueSnapshots));
    }

    private static DashboardDataExportSource CreateSource(IHostEnvironment hostEnvironment)
    {
        return new DashboardDataExportSource(
            "Symphony.Host",
            typeof(DashboardDataExportService).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            hostEnvironment.EnvironmentName);
    }

    private DashboardSessionHistorySnapshot EnsureHistoryMetadata(
        DashboardSessionHistorySnapshot sessionHistory,
        DashboardSnapshot dashboardSnapshot,
        OrchestratorIssueSnapshot? issueSnapshot)
    {
        if (sessionHistory.Metadata is not null)
        {
            return sessionHistory;
        }

        var session = sessionHistory.Session;
        var activeSession = dashboardSnapshot.ActiveSessions
            .FirstOrDefault(candidate => string.Equals(candidate.IssueIdentifier, session.IssueIdentifier, StringComparison.OrdinalIgnoreCase));

        return sessionHistory with
        {
            Metadata = CreateSessionMetadataSnapshot(session, activeSession, issueSnapshot, dashboardSnapshot, retainedMetadata: null)
        };
    }

    private static DashboardSessionMetadataSnapshot CreateSessionMetadataSnapshot(
        SessionRecord session,
        DashboardActiveSessionSnapshot? activeSession,
        OrchestratorIssueSnapshot? issueSnapshot,
        DashboardSnapshot dashboardSnapshot,
        DashboardSessionMetadataSnapshot? retainedMetadata)
    {
        if (retainedMetadata is not null)
        {
            var availabilityMessage = session.IsActive
                ? retainedMetadata.AvailabilityMessage
                : "Finished sessions keep the last known session ID and token totals when available.";
            return retainedMetadata with { AvailabilityMessage = availabilityMessage };
        }

        var recentAttempt = dashboardSnapshot.RecentAttempts
            .Where(attempt => string.Equals(attempt.IssueIdentifier, session.IssueIdentifier, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(attempt => attempt.CompletedAt)
            .FirstOrDefault();
        var runningSession = issueSnapshot?.Running;
        var sessionId = runningSession?.SessionId ?? activeSession?.SessionId ?? recentAttempt?.SessionId;
        var orchestratorSessionId = issueSnapshot?.OrchestratorSessionId ?? recentAttempt?.OrchestratorSessionId;
        var turnCount = runningSession?.TurnCount ?? activeSession?.TurnCount ?? TryParseTurnCount(sessionId);
        var attempt = issueSnapshot is null ? recentAttempt?.Attempt : issueSnapshot.CurrentRetryAttempt;
        var isAttemptKnown = issueSnapshot is not null || recentAttempt is not null;

        return new DashboardSessionMetadataSnapshot(
            runningSession?.InputTokens,
            runningSession?.OutputTokens,
            runningSession?.TotalTokens ?? activeSession?.TotalTokens,
            turnCount,
            sessionId,
            orchestratorSessionId,
            attempt,
            isAttemptKnown,
            GetMetadataAvailabilityMessage(session, runningSession is not null));
    }

    private static string? GetMetadataAvailabilityMessage(SessionRecord session, bool hasRunningSnapshot)
    {
        if (!session.IsActive)
        {
            return "Finished sessions keep the last known session ID and attempt when available. Live token counters are only available while the session is active.";
        }

        return hasRunningSnapshot
            ? null
            : "Live runtime metadata is temporarily unavailable while the active session snapshot catches up.";
    }

    private static int? TryParseTurnCount(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        var markerIndex = sessionId.LastIndexOf("-turn-", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var turnSegment = sessionId[(markerIndex + 6)..];
        return int.TryParse(turnSegment, out var turnCount)
            ? turnCount
            : null;
    }
}
