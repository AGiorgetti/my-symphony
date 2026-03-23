using Microsoft.Extensions.Logging;
using Symphony.Abstractions.Orchestration;
using Symphony.Abstractions.Trackers;
using Symphony.Abstractions.Workspaces;
using Symphony.Application.Configuration;
using Symphony.Application.Orchestration;
using Symphony.Domain.Issues;

namespace Symphony.Application.Polling;

public sealed class OrchestratorPollingIterationHandler(
    IIssueTrackerClient issueTrackerClient,
    OrchestratorDispatchQueue dispatchQueue,
    ActiveSessionRegistry activeSessionRegistry,
    IWorkspaceManager workspaceManager,
    TimeProvider timeProvider,
    ILogger<OrchestratorPollingIterationHandler> logger) : IPollingIterationHandler
{
    public async Task ExecuteAsync(WorkflowServiceOptions workflowOptions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workflowOptions);

        var activeStates = CreateStateSet(workflowOptions.Tracker.ActiveStates);
        var terminalStates = CreateStateSet(workflowOptions.Tracker.TerminalStates);

        await ReconcileRunningIssuesAsync(
                activeStates,
                terminalStates,
                workflowOptions.Codex.StallTimeoutMs,
                cancellationToken)
            .ConfigureAwait(false);

        var candidates = await issueTrackerClient.FetchCandidateIssuesAsync(cancellationToken).ConfigureAwait(false);
        var plannedByState = CountRunningStates(activeSessionRegistry.GetActiveSessions());

        foreach (var issue in SortForDispatch(candidates))
        {
            if (!ShouldDispatch(issue, activeStates, terminalStates, workflowOptions.Agent.MaxConcurrentAgentsByState, plannedByState, out var skipReason))
            {
                logger.LogDebug(
                    "poll_dispatch skipped issue_id={issue_id} issue_identifier={issue_identifier} issue_state={issue_state} reason={reason} outcome=skipped",
                    issue.Id,
                    issue.Identifier,
                    issue.State,
                    skipReason);
                continue;
            }

            var enqueueResult = await dispatchQueue.QueueAsync(issue, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (enqueueResult == DispatchEnqueueResult.NoCapacity)
            {
                logger.LogDebug(
                    "poll_dispatch skipped issue_id={issue_id} issue_identifier={issue_identifier} issue_state={issue_state} reason=no_capacity outcome=skipped",
                    issue.Id,
                    issue.Identifier,
                    issue.State);
                return;
            }

            if (enqueueResult == DispatchEnqueueResult.AlreadyClaimed)
            {
                logger.LogDebug(
                    "poll_dispatch skipped issue_id={issue_id} issue_identifier={issue_identifier} issue_state={issue_state} reason=already_claimed outcome=skipped",
                    issue.Id,
                    issue.Identifier,
                    issue.State);
                continue;
            }

            IncrementStateCount(plannedByState, issue.NormalizedState);
            logger.LogInformation(
                "poll_dispatch completed issue_id={issue_id} issue_identifier={issue_identifier} issue_state={issue_state} outcome=enqueued",
                issue.Id,
                issue.Identifier,
                issue.State);
        }
    }

    private async Task ReconcileRunningIssuesAsync(
        HashSet<string> activeStates,
        HashSet<string> terminalStates,
        int stallTimeoutMs,
        CancellationToken cancellationToken)
    {
        await DetectAndRecoverStalledRunsAsync(stallTimeoutMs, cancellationToken).ConfigureAwait(false);

        var activeSessions = activeSessionRegistry.GetActiveSessions();
        if (activeSessions.Count == 0)
        {
            return;
        }

        IReadOnlyList<Issue> refreshedIssues;
        try
        {
            refreshedIssues = await issueTrackerClient.FetchIssueStatesByIdsAsync(
                    activeSessions.Select(session => session.IssueId).ToArray(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogDebug(
                exception,
                "poll_reconcile failed running_issue_count={running_issue_count} reason=tracker_refresh_failed outcome=skipped",
                activeSessions.Count);
            return;
        }

        foreach (var issue in refreshedIssues)
        {
            if (terminalStates.Contains(issue.NormalizedState))
            {
                var canceled = await activeSessionRegistry.TryCancelAndWaitForReconciliationAsync(
                        issue.Id,
                        issue.State,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!canceled)
                {
                    continue;
                }

                await workspaceManager.DeleteForIssueAsync(issue.Identifier, cancellationToken).ConfigureAwait(false);
                logger.LogInformation(
                    "poll_reconcile completed issue_id={issue_id} issue_identifier={issue_identifier} issue_state={issue_state} cleanup_workspace={cleanup_workspace} outcome=canceled",
                    issue.Id,
                    issue.Identifier,
                    issue.State,
                    true);
                continue;
            }

            if (activeStates.Contains(issue.NormalizedState))
            {
                var sessionUpdated = activeSessionRegistry.TryRefreshIssue(issue);
                var queueUpdated = dispatchQueue.TryRefreshRunningIssue(issue);

                if (sessionUpdated || queueUpdated)
                {
                    logger.LogInformation(
                        "poll_reconcile completed issue_id={issue_id} issue_identifier={issue_identifier} issue_state={issue_state} cleanup_workspace={cleanup_workspace} outcome=refreshed",
                        issue.Id,
                        issue.Identifier,
                        issue.State,
                        false);
                }

                continue;
            }

            var canceledWithoutCleanup = await activeSessionRegistry.TryCancelAndWaitForReconciliationAsync(
                    issue.Id,
                    issue.State,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!canceledWithoutCleanup)
            {
                continue;
            }

            logger.LogInformation(
                "poll_reconcile completed issue_id={issue_id} issue_identifier={issue_identifier} issue_state={issue_state} cleanup_workspace={cleanup_workspace} outcome=canceled",
                issue.Id,
                issue.Identifier,
                issue.State,
                false);
        }
    }

    private async Task DetectAndRecoverStalledRunsAsync(int stallTimeoutMs, CancellationToken cancellationToken)
    {
        if (stallTimeoutMs <= 0)
        {
            return;
        }

        var stallTimeout = TimeSpan.FromMilliseconds(stallTimeoutMs);
        var observedAt = timeProvider.GetUtcNow();

        foreach (var activeSession in activeSessionRegistry.GetActiveSessions())
        {
            var lastActivityAt = activeSession.Session?.LastCodexTimestamp ?? activeSession.StartedAt;
            var elapsed = observedAt - lastActivityAt;
            if (elapsed <= stallTimeout)
            {
                continue;
            }

            var error = $"Session stalled after {Math.Ceiling(elapsed.TotalMilliseconds)} ms of Codex inactivity.";
            var canceled = await activeSessionRegistry.TryMarkStalledAndWaitAsync(
                    activeSession.IssueId,
                    error,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!canceled)
            {
                continue;
            }

            logger.LogWarning(
                "poll_reconcile stalled issue_id={issue_id} issue_identifier={issue_identifier} session_id={session_id} started_at={started_at:O} last_codex_timestamp={last_codex_timestamp:O} elapsed_ms={elapsed_ms} stall_timeout_ms={stall_timeout_ms} outcome=stalled",
                activeSession.IssueId,
                activeSession.IssueIdentifier,
                activeSession.Session?.SessionId,
                activeSession.StartedAt,
                activeSession.Session?.LastCodexTimestamp,
                Math.Ceiling(elapsed.TotalMilliseconds),
                stallTimeoutMs);
        }
    }

    private static IEnumerable<Issue> SortForDispatch(IEnumerable<Issue> issues)
    {
        return issues
            .OrderBy(GetPrioritySortValue)
            .ThenBy(issue => issue.CreatedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(issue => issue.Identifier, StringComparer.Ordinal);
    }

    private static bool ShouldDispatch(
        Issue issue,
        HashSet<string> activeStates,
        HashSet<string> terminalStates,
        IReadOnlyDictionary<string, int> maxConcurrentAgentsByState,
        Dictionary<string, int> plannedByState,
        out string skipReason)
    {
        if (!activeStates.Contains(issue.NormalizedState) || terminalStates.Contains(issue.NormalizedState))
        {
            skipReason = "state_not_dispatchable";
            return false;
        }

        if (string.Equals(issue.NormalizedState, "todo", StringComparison.Ordinal)
            && issue.BlockedBy.Any(blocker => blocker.NormalizedState is null || !terminalStates.Contains(blocker.NormalizedState)))
        {
            skipReason = "blocked_by_dependency";
            return false;
        }

        if (maxConcurrentAgentsByState.TryGetValue(issue.NormalizedState, out var perStateLimit)
            && plannedByState.GetValueOrDefault(issue.NormalizedState) >= perStateLimit)
        {
            skipReason = "state_capacity_exhausted";
            return false;
        }

        skipReason = string.Empty;
        return true;
    }

    private static int GetPrioritySortValue(Issue issue)
    {
        return issue.Priority is >= 1 and <= 4
            ? issue.Priority.Value
            : int.MaxValue;
    }

    private static HashSet<string> CreateStateSet(IEnumerable<string> states)
    {
        return states
            .Where(state => !string.IsNullOrWhiteSpace(state))
            .Select(state => state.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
    }

    private static Dictionary<string, int> CountRunningStates(IEnumerable<ActiveSessionSnapshot> activeSessions)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var activeSession in activeSessions)
        {
            IncrementStateCount(counts, activeSession.IssueState.Trim().ToLowerInvariant());
        }

        return counts;
    }

    private static void IncrementStateCount(IDictionary<string, int> stateCounts, string normalizedState)
    {
        if (stateCounts.TryGetValue(normalizedState, out var currentCount))
        {
            stateCounts[normalizedState] = currentCount + 1;
            return;
        }

        stateCounts[normalizedState] = 1;
    }
}
