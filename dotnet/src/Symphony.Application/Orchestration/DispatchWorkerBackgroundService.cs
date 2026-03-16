using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Symphony.Domain.Runs;

namespace Symphony.Application.Orchestration;

public sealed class DispatchWorkerBackgroundService(
    OrchestratorDispatchQueue dispatchQueue,
    ActiveSessionRegistry activeSessionRegistry,
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
        using var executionLease = dispatchQueue.BeginExecution(workItem);
        using var activeSession = activeSessionRegistry.BeginSession(workItem.Issue, workItem.Attempt, cancellationToken);
        var executionContext = activeSession.CreateExecutionContext();

        try
        {
            await queuedIssueWorker.ExecuteAsync(executionContext).ConfigureAwait(false);
            executionContext.UpdateStatus(RunAttemptStatus.Succeeded);
        }
        catch (OperationCanceledException) when (activeSession.WasCanceledByReconciliation)
        {
            executionContext.UpdateStatus(
                RunAttemptStatus.CanceledByReconciliation,
                "Execution canceled after reconciliation marked the issue ineligible.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(
                "Queued execution for issue {IssueIdentifier} was canceled during shutdown.",
                workItem.Issue.Identifier);
        }
        catch (Exception exception)
        {
            executionContext.UpdateStatus(RunAttemptStatus.Failed, exception.Message);
            logger.LogError(
                exception,
                "Queued execution for issue {IssueIdentifier} failed.",
                workItem.Issue.Identifier);
        }
    }
}
