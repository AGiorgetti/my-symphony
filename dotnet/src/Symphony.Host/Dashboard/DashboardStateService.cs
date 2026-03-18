using Symphony.Application.Polling;
using Symphony.Application.Runtime;

namespace Symphony.Host.Dashboard;

public sealed class DashboardStateService(
    IOrchestratorRuntimeService orchestratorRuntimeService,
    AttemptHistoryTracker attemptHistoryTracker,
    PollingStatusTracker pollingStatusTracker) : IDashboardStateService
{
    private const string InMemoryMode = "Single-process in-memory";

    public async Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var runtimeSnapshot = await orchestratorRuntimeService.GetStateSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var pollingSnapshot = pollingStatusTracker.GetSnapshot();
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

        return new DashboardSnapshot(
            runtimeSnapshot.GeneratedAt,
            DetermineServiceHealth(pollingSnapshot),
            InMemoryMode,
            pollingSnapshot.LastSuccessfulTickAt ?? pollingSnapshot.LastCompletedAt ?? pollingSnapshot.LastStartedAt,
            runtimeSnapshot.Running.Count,
            runtimeSnapshot.Retrying.Count,
            runtimeSnapshot.CodexTotals.InputTokens,
            runtimeSnapshot.CodexTotals.OutputTokens,
            runtimeSnapshot.CodexTotals.TotalTokens,
            runtimeSnapshot.CodexTotals.SecondsRunning,
            activeSessions,
            retryQueue,
            recentAttempts,
            pollingSnapshot.LastError);
    }

    private static string DetermineServiceHealth(PollingStatusSnapshot pollingSnapshot)
    {
        if (pollingSnapshot.LastFailedAt is not null
            && (pollingSnapshot.LastSuccessfulTickAt is null || pollingSnapshot.LastFailedAt > pollingSnapshot.LastSuccessfulTickAt))
        {
            return "Degraded";
        }

        if (pollingSnapshot.LastSuccessfulTickAt is not null)
        {
            return "Healthy";
        }

        return "Starting";
    }
}
