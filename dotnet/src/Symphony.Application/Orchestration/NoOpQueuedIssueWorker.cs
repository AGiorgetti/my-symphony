using Symphony.Domain.Issues;

namespace Symphony.Application.Orchestration;

public sealed class NoOpQueuedIssueWorker : IQueuedIssueWorker
{
    public Task ExecuteAsync(Issue issue, int? attempt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issue);

        return Task.CompletedTask;
    }
}
