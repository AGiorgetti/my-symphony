namespace Symphony.Application.Configuration;

public interface IWorkflowOptionsProvider
{
    Task<WorkflowServiceOptions> GetCurrentAsync(CancellationToken cancellationToken = default);
}
