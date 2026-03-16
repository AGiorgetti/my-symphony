using Symphony.Domain.Issues;

namespace Symphony.Application.Orchestration;

public interface IQueuedIssueWorker
{
    Task ExecuteAsync(Issue issue, int? attempt, CancellationToken cancellationToken);
}
