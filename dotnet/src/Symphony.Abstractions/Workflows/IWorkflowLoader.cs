using Symphony.Domain.Workflows;

namespace Symphony.Abstractions.Workflows;

public interface IWorkflowLoader
{
    Task<WorkflowDefinition> LoadAsync(string workflowPath, CancellationToken cancellationToken = default);
}
