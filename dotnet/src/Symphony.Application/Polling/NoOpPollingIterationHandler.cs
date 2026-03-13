using Symphony.Application.Configuration;

namespace Symphony.Application.Polling;

public sealed class NoOpPollingIterationHandler : IPollingIterationHandler
{
    public Task ExecuteAsync(WorkflowServiceOptions workflowOptions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workflowOptions);

        return Task.CompletedTask;
    }
}
