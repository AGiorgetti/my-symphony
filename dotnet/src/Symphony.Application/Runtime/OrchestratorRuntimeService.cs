using Symphony.Abstractions.Orchestration;
using Symphony.Application.Polling;
using Symphony.Application.Orchestration;

namespace Symphony.Application.Runtime;

public sealed class OrchestratorRuntimeService(
    IActiveSessionRegistry activeSessionRegistry,
    IOrchestratorDispatchStatusReader dispatchStatusReader,
    PollingRefreshTrigger pollingRefreshTrigger,
    TimeProvider timeProvider,
    FollowUpActionRegistry? followUpActionRegistry = null) : IOrchestratorRuntimeService
{
    public Task<OrchestratorStateSnapshot> GetStateSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var generatedAt = timeProvider.GetUtcNow();
        var dispatchSnapshot = dispatchStatusReader.GetSnapshot();
        var running = activeSessionRegistry.GetActiveSessions()
            .Select(CreateRunningIssueSnapshot)
            .ToArray();

        return Task.FromResult(
            new OrchestratorStateSnapshot(
                generatedAt,
                running,
                dispatchSnapshot.Retrying,
                new CodexTotalsSnapshot(
                    running.Sum(session => session.InputTokens),
                    running.Sum(session => session.OutputTokens),
                    running.Sum(session => session.TotalTokens),
                    running.Sum(session => Math.Max((generatedAt - session.StartedAt).TotalSeconds, 0d))),
                RateLimits: null,
                Blocked: dispatchSnapshot.Blocked,
                FollowUpActions: followUpActionRegistry?.GetAll() ?? Array.Empty<FollowUpActionSnapshot>()));
    }

    public Task<OrchestratorIssueSnapshot?> GetIssueSnapshotAsync(
        string issueIdentifier,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issueIdentifier);

        var normalizedIssueIdentifier = issueIdentifier.Trim();
        var activeSession = activeSessionRegistry.GetActiveSessions()
            .FirstOrDefault(session => string.Equals(session.IssueIdentifier, normalizedIssueIdentifier, StringComparison.OrdinalIgnoreCase));
        if (activeSession is not null)
        {
            return Task.FromResult<OrchestratorIssueSnapshot?>(
                CreateIssueSnapshot(
                    activeSession.IssueIdentifier,
                    activeSession.IssueId,
                    "running",
                    activeSession.Attempt,
                    CreateRunningIssueSnapshot(activeSession),
                    retry: null,
                    activeSession.Error,
                    CreateRecentEvents(activeSession),
                    orchestratorSessionId: activeSession.OrchestratorSessionId,
                    blocked: null,
                    followUpActions: followUpActionRegistry?.GetByIssueIdentifier(activeSession.IssueIdentifier) ?? Array.Empty<FollowUpActionSnapshot>()));
        }

        var dispatchSnapshot = dispatchStatusReader.GetSnapshot();

        var retryingIssue = dispatchSnapshot.Retrying
            .FirstOrDefault(issue => string.Equals(issue.IssueIdentifier, normalizedIssueIdentifier, StringComparison.OrdinalIgnoreCase));
        if (retryingIssue is not null)
        {
            return Task.FromResult<OrchestratorIssueSnapshot?>(
                CreateIssueSnapshot(
                    retryingIssue.IssueIdentifier,
                    retryingIssue.IssueId,
                    "retrying",
                    retryingIssue.Attempt,
                    running: null,
                    retryingIssue,
                    retryingIssue.Error,
                    [],
                    orchestratorSessionId: null,
                    blocked: null,
                    followUpActions: followUpActionRegistry?.GetByIssueIdentifier(retryingIssue.IssueIdentifier) ?? Array.Empty<FollowUpActionSnapshot>()));
        }

        var blockedIssue = dispatchSnapshot.Blocked
            .FirstOrDefault(issue => string.Equals(issue.IssueIdentifier, normalizedIssueIdentifier, StringComparison.OrdinalIgnoreCase));
        if (blockedIssue is not null)
        {
            return Task.FromResult<OrchestratorIssueSnapshot?>(
                CreateIssueSnapshot(
                    blockedIssue.IssueIdentifier,
                    blockedIssue.IssueId,
                    "blocked_error",
                    blockedIssue.Attempt,
                    running: null,
                    retry: null,
                    blockedIssue.ErrorMessage,
                    [],
                    orchestratorSessionId: blockedIssue.OrchestratorSessionId,
                    blocked: blockedIssue,
                    followUpActions: followUpActionRegistry?.GetByIssueIdentifier(blockedIssue.IssueIdentifier) ?? Array.Empty<FollowUpActionSnapshot>()));
        }

        var queuedIssue = dispatchSnapshot.Queued
            .FirstOrDefault(issue => string.Equals(issue.IssueIdentifier, normalizedIssueIdentifier, StringComparison.OrdinalIgnoreCase));
        if (queuedIssue is not null)
        {
            return Task.FromResult<OrchestratorIssueSnapshot?>(
                CreateIssueSnapshot(
                    queuedIssue.IssueIdentifier,
                    queuedIssue.IssueId,
                    "queued",
                    queuedIssue.Attempt,
                    running: null,
                    retry: null,
                    lastError: null,
                    [],
                    orchestratorSessionId: null,
                    blocked: null,
                    followUpActions: followUpActionRegistry?.GetByIssueIdentifier(queuedIssue.IssueIdentifier) ?? Array.Empty<FollowUpActionSnapshot>()));
        }

        return Task.FromResult<OrchestratorIssueSnapshot?>(null);
    }

    public PollingRefreshReceipt RequestRefresh()
    {
        return pollingRefreshTrigger.RequestRefresh();
    }

    private static OrchestratorIssueSnapshot CreateIssueSnapshot(
        string issueIdentifier,
        string issueId,
        string status,
        int? attempt,
        RunningIssueSnapshot? running,
        RetryDispatchSnapshot? retry,
        string? lastError,
        IReadOnlyList<RuntimeEventSnapshot> recentEvents,
        string? orchestratorSessionId = null,
        BlockedDispatchSnapshot? blocked = null,
        IReadOnlyList<FollowUpActionSnapshot>? followUpActions = null)
    {
        var normalizedAttempt = attempt.GetValueOrDefault();

        return new OrchestratorIssueSnapshot(
            issueIdentifier,
            issueId,
            status,
            RestartCount: Math.Max(normalizedAttempt - 1, 0),
            CurrentRetryAttempt: attempt,
            running,
            retry,
            lastError,
            recentEvents,
            orchestratorSessionId,
            blocked,
            followUpActions ?? Array.Empty<FollowUpActionSnapshot>());
    }

    private static RunningIssueSnapshot CreateRunningIssueSnapshot(ActiveSessionSnapshot activeSession)
    {
        return new RunningIssueSnapshot(
            activeSession.IssueId,
            activeSession.IssueIdentifier,
            activeSession.IssueState,
            activeSession.Session?.SessionId,
            activeSession.Session?.TurnCount ?? 0,
            activeSession.Session?.LastCodexEvent,
            activeSession.Session?.LastCodexMessage,
            activeSession.StartedAt,
            activeSession.Session?.LastCodexTimestamp,
            activeSession.Session?.CodexInputTokens ?? 0,
            activeSession.Session?.CodexOutputTokens ?? 0,
            activeSession.Session?.CodexTotalTokens ?? 0,
            activeSession.OrchestratorSessionId);
    }

    private static IReadOnlyList<RuntimeEventSnapshot> CreateRecentEvents(ActiveSessionSnapshot activeSession)
    {
        if (activeSession.Session?.LastCodexTimestamp is null)
        {
            return Array.Empty<RuntimeEventSnapshot>();
        }

        return
        [
            new RuntimeEventSnapshot(
                activeSession.Session.LastCodexTimestamp.Value,
                activeSession.Session.LastCodexEvent,
                activeSession.Session.LastCodexMessage)
        ];
    }
}
