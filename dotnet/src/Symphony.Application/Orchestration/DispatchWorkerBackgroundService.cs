using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Symphony.Application.Runtime;
using Symphony.Domain.Runs;

namespace Symphony.Application.Orchestration;

public sealed class DispatchWorkerBackgroundService(
    OrchestratorDispatchQueue dispatchQueue,
    ActiveSessionRegistry activeSessionRegistry,
    AttemptHistoryTracker attemptHistoryTracker,
    TimeProvider timeProvider,
    IQueuedIssueWorker queuedIssueWorker,
    ILogger<DispatchWorkerBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var inFlightTasks = new List<Task>();

        try
        {
            await foreach (var workItem in dispatchQueue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
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
                sessionId: executionContext.SessionId);
            await dispatchQueue.ScheduleContinuationRetryAsync(workItem, cancellationToken).ConfigureAwait(false);
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
                executionContext.SessionId);
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
                executionContext.SessionId);
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
                executionContext.SessionId);
            logger.LogWarning(
                exception,
                "dispatch_execution timed_out issue_id={issue_id} issue_identifier={issue_identifier} session_id={session_id} reason=timeout outcome=timed_out",
                workItem.Issue.Id,
                workItem.Issue.Identifier,
                executionContext.SessionId);
            await dispatchQueue.ScheduleFailureRetryAsync(workItem, exception, cancellationToken).ConfigureAwait(false);
            executionLease.PreserveClaimForRetry();
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
                executionContext.SessionId);
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
}
