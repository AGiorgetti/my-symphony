using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Symphony.Application.Orchestration;

public sealed class DispatchWorkerBackgroundService(
    OrchestratorDispatchQueue dispatchQueue,
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

        try
        {
            await queuedIssueWorker.ExecuteAsync(workItem.Issue, workItem.Attempt, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(
                "Queued execution for issue {IssueIdentifier} was canceled during shutdown.",
                workItem.Issue.Identifier);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Queued execution for issue {IssueIdentifier} failed.",
                workItem.Issue.Identifier);
        }
    }
}
