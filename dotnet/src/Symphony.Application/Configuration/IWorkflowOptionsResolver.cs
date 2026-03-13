using Symphony.Domain.Workflows;

namespace Symphony.Application.Configuration;

public interface IWorkflowOptionsResolver
{
    WorkflowServiceOptions Resolve(WorkflowDefinition workflowDefinition);
}
