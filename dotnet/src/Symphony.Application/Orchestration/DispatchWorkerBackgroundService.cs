using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Symphony.Abstractions.Orchestration;
using Symphony.Abstractions.Trackers;
using Symphony.Application.Configuration;
using Symphony.Application.Runtime;
using Symphony.Domain.Issues;
using Symphony.Domain.Runs;

namespace Symphony.Application.Orchestration;

public sealed class DispatchWorkerBackgroundService(
    OrchestratorDispatchQueue dispatchQueue,
    IOrchestratorExecutionGate orchestratorExecutionGate,
    ActiveSessionRegistry activeSessionRegistry,
    AttemptHistoryTracker attemptHistoryTracker,
    IIssueTrackerClient issueTrackerClient,
    IWorkflowOptionsProvider workflowOptionsProvider,
    TimeProvider timeProvider,
    IQueuedIssueWorker queuedIssueWorker,
    ILogger<DispatchWorkerBackgroundService> logger,
    FollowUpActionRegistry? followUpActionRegistry = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var inFlightTasks = new List<Task>();

        try
        {
            await foreach (var workItem in dispatchQueue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await orchestratorExecutionGate.WaitUntilStartedAsync(stoppingToken).ConfigureAwait(false);
                var executionTask = ExecuteQueuedWorkItemAsync(workItem, stoppingToken);

                lock (inFlightTasks)
                {
                    inFlightTasks.Add(executionTask);
                }

                _ = executionTask.ContinueWith(
                    completedTask =>
                    {
                        lock (inFlightTasks)
                        {
                            inFlightTasks.Remove(completedTask);
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        Task[] tasksToAwait;
        lock (inFlightTasks)
        {
            tasksToAwait = inFlightTasks.ToArray();
        }

        await Task.WhenAll(tasksToAwait).ConfigureAwait(false);
    }

    private async Task ExecuteQueuedWorkItemAsync(
        OrchestratorDispatchQueue.DispatchQueueWorkItem workItem,
        CancellationToken cancellationToken)
    {
        var attemptStartedAt = timeProvider.GetUtcNow();

        logger.LogInformation(
            "dispatch_worker started issue_id={issue_id} issue_identifier={issue_identifier} attempt={attempt} outcome=started",
            workItem.Issue.Id,
            workItem.Issue.Identifier,
            workItem.Attempt);

        using var executionLease = dispatchQueue.BeginExecution(workItem);
        using var activeSession = activeSessionRegistry.BeginSession(workItem.Issue, workItem.Attempt, cancellationToken);
        var executionContext = activeSession.CreateExecutionContext();

        try
        {
            await queuedIssueWorker.ExecuteAsync(executionContext).ConfigureAwait(false);
            executionContext.UpdateStatus(RunAttemptStatus.Succeeded);
            attemptHistoryTracker.Record(
                workItem.Issue.Id,
                workItem.Issue.Identifier,
                workItem.Attempt,
                "Succeeded",
                attemptStartedAt,
                timeProvider.GetUtcNow(),
                sessionId: executionContext.SessionId,
                orchestratorSessionId: executionContext.OrchestratorSessionId);
            var continuationIssue = await TryResolveContinuationIssueAsync(workItem.Issue, cancellationToken).ConfigureAwait(false);
            if (continuationIssue is not null)
            {
                await dispatchQueue.ScheduleContinuationRetryAsync(workItem with { Issue = continuationIssue }, cancellationToken).ConfigureAwait(false);
                executionLease.PreserveClaimForRetry();
            }
        }
        catch (OperationCanceledException) when (activeSession.WasCanceledByStall)
        {
            var stallError = activeSession.CancellationError
                ?? "Execution canceled after reconciliation detected a stalled Codex session.";
            var stallException = new IssueExecutionStalledException(stallError);

            executionContext.UpdateStatus(RunAttemptStatus.Stalled, stallError);
            attemptHistoryTracker.Record(
                workItem.Issue.Id,
                workItem.Issue.Identifier,
                workItem.Attempt,
                "Stalled",
                attemptStartedAt,
                timeProvider.GetUtcNow(),
                stallError,
                executionContext.SessionId,
                executionContext.OrchestratorSessionId);
            logger.LogWarning(
                stallException,
                "dispatch_execution stalled issue_id={issue_id} issue_identifier={issue_identifier} session_id={session_id} reason=stall outcome=stalled",
                workItem.Issue.Id,
                workItem.Issue.Identifier,
                executionContext.SessionId);
            await dispatchQueue.ScheduleFailureRetryAsync(workItem, stallException, cancellationToken).ConfigureAwait(false);
            executionLease.PreserveClaimForRetry();
        }
        catch (OperationCanceledException) when (activeSession.WasCanceledByReconciliation)
        {
            const string cancellationError = "Execution canceled after reconciliation marked the issue ineligible.";
            executionContext.UpdateStatus(
                RunAttemptStatus.CanceledByReconciliation,
                cancellationError);
            attemptHistoryTracker.Record(
                workItem.Issue.Id,
                workItem.Issue.Identifier,
                workItem.Attempt,
                "Canceled",
                attemptStartedAt,
                timeProvider.GetUtcNow(),
                cancellationError,
                executionContext.SessionId,
                executionContext.OrchestratorSessionId);
            logger.LogInformation(
                "dispatch_execution canceled issue_id={issue_id} issue_identifier={issue_identifier} session_id={session_id} reason=reconciliation outcome=canceled",
                workItem.Issue.Id,
                workItem.Issue.Identifier,
                executionContext.SessionId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(
                "dispatch_execution canceled issue_id={issue_id} issue_identifier={issue_identifier} session_id={session_id} reason=shutdown outcome=canceled",
                workItem.Issue.Id,
                workItem.Issue.Identifier,
                executionContext.SessionId);
        }
        catch (NonTransientIssueExecutionException exception)
        {
            executionContext.UpdateStatus(RunAttemptStatus.Failed, exception.Message);
            attemptHistoryTracker.Record(
                workItem.Issue.Id,
                workItem.Issue.Identifier,
                workItem.Attempt,
                "Failed",
                attemptStartedAt,
                timeProvider.GetUtcNow(),
                exception.Message,
                executionContext.SessionId,
                executionContext.OrchestratorSessionId);
            logger.LogWarning(
                exception,
                "dispatch_execution failed issue_id={issue_id} issue_identifier={issue_identifier} session_id={session_id} reason=non_transient outcome=failed_no_retry",
                workItem.Issue.Id,
                workItem.Issue.Identifier,
                executionContext.SessionId);
        }
        catch (IssueExecutionTimedOutException exception)
        {
            executionContext.UpdateStatus(RunAttemptStatus.TimedOut, exception.Message);
            attemptHistoryTracker.Record(
                workItem.Issue.Id,
                workItem.Issue.Identifier,
                workItem.Attempt,
                "TimedOut",
                attemptStartedAt,
                timeProvider.GetUtcNow(),
                exception.Message,
                executionContext.SessionId,
                executionContext.OrchestratorSessionId);
            logger.LogWarning(
                exception,
                "dispatch_execution timed_out issue_id={issue_id} issue_identifier={issue_identifier} session_id={session_id} reason=timeout outcome=timed_out",
                workItem.Issue.Id,
                workItem.Issue.Identifier,
                executionContext.SessionId);
            await dispatchQueue.ScheduleFailureRetryAsync(workItem, exception, cancellationToken).ConfigureAwait(false);
            executionLease.PreserveClaimForRetry();
        }
        catch (IssueBlockingException exception)
        {
            await HandleBlockedIssueAsync(
                    executionContext,
                    workItem,
                    executionLease,
                    exception,
                    attemptStartedAt,
                    followUpActionRegistry ?? new FollowUpActionRegistry(timeProvider))
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception) when (TryCreateProcessStartBlockingException(exception, out _))
        {
            var blockingException = TryCreateProcessStartBlockingException(exception, out var resolvedException)
                ? resolvedException
                : throw new InvalidOperationException("Expected blocking exception to be available.");
            await HandleBlockedIssueAsync(
                    executionContext,
                    workItem,
                    executionLease,
                    blockingException,
                    attemptStartedAt,
                    followUpActionRegistry ?? new FollowUpActionRegistry(timeProvider))
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            executionContext.UpdateStatus(RunAttemptStatus.Failed, exception.Message);
            attemptHistoryTracker.Record(
                workItem.Issue.Id,
                workItem.Issue.Identifier,
                workItem.Attempt,
                "Retrying",
                attemptStartedAt,
                timeProvider.GetUtcNow(),
                exception.Message,
                executionContext.SessionId,
                executionContext.OrchestratorSessionId);
            logger.LogError(
                exception,
                "dispatch_execution failed issue_id={issue_id} issue_identifier={issue_identifier} session_id={session_id} reason=worker_exception outcome=failed",
                workItem.Issue.Id,
                workItem.Issue.Identifier,
                executionContext.SessionId);
            await dispatchQueue.ScheduleFailureRetryAsync(workItem, exception, cancellationToken).ConfigureAwait(false);
            executionLease.PreserveClaimForRetry();
        }
    }

    private async Task HandleBlockedIssueAsync(
        QueuedIssueExecutionContext executionContext,
        OrchestratorDispatchQueue.DispatchQueueWorkItem workItem,
        OrchestratorDispatchQueue.ExecutionLease executionLease,
        IssueBlockingException exception,
        DateTimeOffset attemptStartedAt,
        FollowUpActionRegistry followUpActionRegistry)
    {
        executionContext.UpdateStatus(RunAttemptStatus.BlockedError, exception.Message);
        attemptHistoryTracker.Record(
            workItem.Issue.Id,
            workItem.Issue.Identifier,
            workItem.Attempt,
            "BlockedError",
            attemptStartedAt,
            timeProvider.GetUtcNow(),
            exception.Message,
            executionContext.SessionId,
            executionContext.OrchestratorSessionId);

        var followUpAction = followUpActionRegistry.CreatePending(
            executionContext.Issue.Id,
            executionContext.Issue.Identifier,
            executionContext.OrchestratorSessionId,
            exception.ReasonCode,
            exception.Message,
            exception.RequiredUserAction,
            exception.Options);

        dispatchQueue.BlockAsync(
            executionContext.Issue,
            executionContext.OrchestratorSessionId,
            workItem.Attempt,
            exception.ReasonCode,
            exception.Message,
            exception.RequiredUserAction,
            followUpAction.FollowUpActionId);
        executionLease.MarkBlocked();

        logger.LogWarning(
            exception,
            "dispatch_execution blocked issue_id={issue_id} issue_identifier={issue_identifier} orchestrator_session_id={orchestrator_session_id} session_id={session_id} reason_code={reason_code} follow_up_action_id={follow_up_action_id} outcome=blocked_error",
            workItem.Issue.Id,
            workItem.Issue.Identifier,
            executionContext.OrchestratorSessionId,
            executionContext.SessionId,
            exception.ReasonCode,
            followUpAction.FollowUpActionId);

        await Task.CompletedTask;
    }

    private static bool TryCreateProcessStartBlockingException(
        InvalidOperationException exception,
        out IssueBlockingException blockingException)
    {
        if (exception.Message.StartsWith("Failed to start process", StringComparison.Ordinal))
        {
            blockingException = new IssueBlockingException(
                BlockingReasonCode.ToolUnavailable,
                exception.Message,
                "Install or expose the required process on PATH, then resolve the follow-up action to resume the run.",
                innerException: exception);
            return true;
        }

        blockingException = null!;
        return false;
    }

    private async Task<Issue?> TryResolveContinuationIssueAsync(Issue issue, CancellationToken cancellationToken)
    {
        IReadOnlyList<Issue> refreshedIssues;
        try
        {
            refreshedIssues = await issueTrackerClient.FetchIssueStatesByIdsAsync([issue.Id], cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "dispatch_continuation skipped issue_id={issue_id} issue_identifier={issue_identifier} reason=tracker_refresh_failed outcome=skipped",
                issue.Id,
                issue.Identifier);
            return null;
        }

        var refreshedIssue = refreshedIssues.FirstOrDefault(candidate => string.Equals(candidate.Id, issue.Id, StringComparison.Ordinal));
        if (refreshedIssue is null)
        {
            logger.LogInformation(
                "dispatch_continuation skipped issue_id={issue_id} issue_identifier={issue_identifier} reason=issue_missing outcome=skipped",
                issue.Id,
                issue.Identifier);
            return null;
        }

        var workflowOptions = await workflowOptionsProvider.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (!IssueDispatchEligibility.CanDispatch(refreshedIssue, workflowOptions, out var skipReason))
        {
            logger.LogInformation(
                "dispatch_continuation skipped issue_id={issue_id} issue_identifier={issue_identifier} issue_state={issue_state} reason={reason} outcome=skipped",
                refreshedIssue.Id,
                refreshedIssue.Identifier,
                refreshedIssue.State,
                skipReason);
            return null;
        }

        return refreshedIssue;
    }
}
