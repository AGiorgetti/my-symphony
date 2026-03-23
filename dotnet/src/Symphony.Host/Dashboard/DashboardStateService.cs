using Symphony.Application.Polling;
using Symphony.Application.Runtime;
using Symphony.Application.Configuration;
using Symphony.Host.Health;

namespace Symphony.Host.Dashboard;

public sealed class DashboardStateService(
    IOrchestratorRuntimeService orchestratorRuntimeService,
    AttemptHistoryTracker attemptHistoryTracker,
    ServiceHealthSnapshotProvider serviceHealthSnapshotProvider,
    IWorkflowOptionsProvider workflowOptionsProvider,
    ISessionActivityStore sessionActivityStore) : IDashboardStateService
{
    private const string InMemoryMode = "Single-process in-memory";
    private readonly Lock _snapshotLock = new();
    private DashboardSnapshot? _lastSnapshot;

    public async Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var runtimeSnapshot = await orchestratorRuntimeService.GetStateSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var workflowOptions = await workflowOptionsProvider.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        var healthSnapshot = serviceHealthSnapshotProvider.GetSnapshot();
        var activeSessions = runtimeSnapshot.Running
            .Select(
                session => new DashboardActiveSessionSnapshot(
                    session.IssueIdentifier,
                    session.State,
                    session.SessionId,
                    session.TurnCount,
                    session.LastEvent,
                    session.LastMessage,
                    session.StartedAt,
                    session.LastEventAt,
                    session.TotalTokens))
            .ToArray();
        var retryQueue = runtimeSnapshot.Retrying
            .Select(
                retry => new DashboardRetrySnapshot(
                    retry.IssueIdentifier,
                    retry.Attempt,
                    retry.DueAt,
                    retry.Error))
            .ToArray();
        var recentAttempts = attemptHistoryTracker.GetRecentAttempts()
            .Select(
                attempt => new DashboardRecentAttemptSnapshot(
                    attempt.IssueIdentifier,
                    attempt.Attempt,
                    attempt.Outcome,
                    attempt.CompletedAt,
                    attempt.DurationSeconds,
                    attempt.Error,
                    attempt.SessionId))
            .ToArray();

        var snapshot = new DashboardSnapshot(
            runtimeSnapshot.GeneratedAt,
            healthSnapshot.Status,
            InMemoryMode,
            healthSnapshot.OrchestratorState,
            healthSnapshot.OrchestratorStateChangedAt,
            healthSnapshot.LastPollTickAt,
            healthSnapshot.LastSuccessfulPollAt,
            healthSnapshot.LastSuccessfulPollAgeSeconds,
            healthSnapshot.WorkflowLoadStatus,
            healthSnapshot.WorkflowLastLoadedAt,
            runtimeSnapshot.Running.Count,
            runtimeSnapshot.Retrying.Count,
            runtimeSnapshot.CodexTotals.InputTokens,
            runtimeSnapshot.CodexTotals.OutputTokens,
            runtimeSnapshot.CodexTotals.TotalTokens,
            runtimeSnapshot.CodexTotals.SecondsRunning,
            activeSessions,
            retryQueue,
            recentAttempts,
            healthSnapshot.PollLastError,
            healthSnapshot.WorkflowLastError,
            workflowOptions.Agent.RequireExecMarker,
            workflowOptions.Agent.ExecMarker);

        lock (_snapshotLock)
        {
            DiffSnapshot(_lastSnapshot, snapshot);
            _lastSnapshot = snapshot;
        }

        return snapshot;
    }

    private void DiffSnapshot(DashboardSnapshot? previousSnapshot, DashboardSnapshot currentSnapshot)
    {
        var previousRunning = (previousSnapshot?.ActiveSessions ?? [])
            .ToDictionary(session => session.IssueIdentifier, StringComparer.OrdinalIgnoreCase);
        var currentRunning = currentSnapshot.ActiveSessions
            .ToDictionary(session => session.IssueIdentifier, StringComparer.OrdinalIgnoreCase);
        var previousRetry = new HashSet<DashboardRetrySnapshot>(previousSnapshot?.RetryQueue ?? []);
        var previousAttempts = new HashSet<DashboardRecentAttemptSnapshot>(previousSnapshot?.RecentAttempts ?? []);
        var latestAttemptsByIssue = currentSnapshot.RecentAttempts
            .GroupBy(attempt => attempt.IssueIdentifier, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(attempt => attempt.CompletedAt).First(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var currentSession in currentSnapshot.ActiveSessions)
        {
            if (!previousRunning.TryGetValue(currentSession.IssueIdentifier, out var previousSession))
            {
                sessionActivityStore.RecordSessionStart(currentSession.IssueIdentifier, currentSession.StartedAt);
                sessionActivityStore.RecordActivity(
                    currentSession.IssueIdentifier,
                    new SessionActivityEntry(
                        SessionActivityKind.LifecycleMilestone,
                        currentSession.LastEventAt ?? currentSession.StartedAt,
                        "Session started",
                        currentSession.State));
                continue;
            }

            if (!string.Equals(previousSession.State, currentSession.State, StringComparison.Ordinal))
            {
                sessionActivityStore.RecordActivity(
                    currentSession.IssueIdentifier,
                    new SessionActivityEntry(
                        SessionActivityKind.LifecycleMilestone,
                        currentSession.LastEventAt ?? currentSnapshot.GeneratedAt,
                        currentSession.State,
                        null));
            }

            if (currentSession.TurnCount > previousSession.TurnCount)
            {
                for (var turn = previousSession.TurnCount + 1; turn <= currentSession.TurnCount; turn++)
                {
                    sessionActivityStore.RecordActivity(
                        currentSession.IssueIdentifier,
                        new SessionActivityEntry(
                            SessionActivityKind.ProgressUpdate,
                            currentSession.LastEventAt ?? currentSnapshot.GeneratedAt,
                            $"Turn {turn}",
                            currentSession.LastMessage));
                }
            }

            if (!string.Equals(previousSession.LastEvent, currentSession.LastEvent, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(currentSession.LastEvent))
            {
                sessionActivityStore.RecordActivity(
                    currentSession.IssueIdentifier,
                    new SessionActivityEntry(
                        SessionActivityKind.AgentMessage,
                        currentSession.LastEventAt ?? currentSnapshot.GeneratedAt,
                        currentSession.LastEvent,
                        currentSession.LastMessage));
            }
        }

        foreach (var previousSession in previousRunning.Values)
        {
            if (currentRunning.ContainsKey(previousSession.IssueIdentifier))
            {
                continue;
            }

            if (!latestAttemptsByIssue.TryGetValue(previousSession.IssueIdentifier, out var latestAttempt))
            {
                latestAttempt = currentSnapshot.RecentAttempts
                    .Where(attempt => string.Equals(attempt.IssueIdentifier, previousSession.IssueIdentifier, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(attempt => attempt.CompletedAt)
                    .FirstOrDefault();
            }

            sessionActivityStore.RecordSessionEnd(
                previousSession.IssueIdentifier,
                latestAttempt?.CompletedAt ?? currentSnapshot.GeneratedAt,
                latestAttempt?.Outcome ?? "Completed",
                latestAttempt?.Error);
        }

        foreach (var retry in currentSnapshot.RetryQueue)
        {
            if (previousRetry.Contains(retry))
            {
                continue;
            }

            sessionActivityStore.RecordActivity(
                retry.IssueIdentifier,
                new SessionActivityEntry(
                    SessionActivityKind.Warning,
                    retry.DueAt,
                    "Queued for retry",
                    retry.Error));
        }

        foreach (var attempt in currentSnapshot.RecentAttempts)
        {
            if (previousAttempts.Contains(attempt))
            {
                continue;
            }

            var existingSession = sessionActivityStore.GetSession(attempt.IssueIdentifier);
            if (existingSession is null)
            {
                sessionActivityStore.RecordSessionStart(
                    attempt.IssueIdentifier,
                    attempt.CompletedAt.AddSeconds(-attempt.DurationSeconds));
            }

            sessionActivityStore.RecordActivity(
                attempt.IssueIdentifier,
                new SessionActivityEntry(
                    SessionActivityKind.Outcome,
                    attempt.CompletedAt,
                    attempt.Outcome,
                    attempt.Error));

            if (existingSession is null || existingSession.IsActive)
            {
                sessionActivityStore.RecordSessionEnd(
                    attempt.IssueIdentifier,
                    attempt.CompletedAt,
                    attempt.Outcome,
                    attempt.Error);
            }
        }
    }
}
