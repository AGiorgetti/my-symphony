namespace Symphony.Application.Orchestration;

public interface IQueuedIssueWorker
{
    Task ExecuteAsync(QueuedIssueExecutionContext context);
}
