using Symphony.Application.Configuration;

namespace Symphony.Application.Polling;

public interface IPollingIterationHandler
{
    Task ExecuteAsync(WorkflowServiceOptions workflowOptions, CancellationToken cancellationToken);
}
