using Symphony.Application.Polling;
using Symphony.Application.Runtime;
using Symphony.Host.Health;

namespace Symphony.Host.Dashboard;

public sealed class DashboardStateService(
    IOrchestratorRuntimeService orchestratorRuntimeService,
    AttemptHistoryTracker attemptHistoryTracker,
    ServiceHealthSnapshotProvider serviceHealthSnapshotProvider) : IDashboardStateService
{
    private const string InMemoryMode = "Single-process in-memory";

    public async Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var runtimeSnapshot = await orchestratorRuntimeService.GetStateSnapshotAsync(cancellationToken).ConfigureAwait(false);
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

        return new DashboardSnapshot(
            runtimeSnapshot.GeneratedAt,
            healthSnapshot.Status,
            InMemoryMode,
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
            healthSnapshot.WorkflowLastError);
    }
}
