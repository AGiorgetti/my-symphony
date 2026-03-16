using Symphony.Domain.Workflows;

namespace Symphony.Application.Configuration;

public interface IWorkflowDefinitionProvider
{
    Task<WorkflowDefinition> GetCurrentDefinitionAsync(CancellationToken cancellationToken = default);
}
