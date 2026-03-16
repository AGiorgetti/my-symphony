namespace Symphony.Application.Orchestration;

public sealed class NoOpQueuedIssueWorker : IQueuedIssueWorker
{
    public Task ExecuteAsync(QueuedIssueExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Task.CompletedTask;
    }
}
