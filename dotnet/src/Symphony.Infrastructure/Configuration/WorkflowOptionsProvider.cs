using Symphony.Abstractions.Workflows;
using Symphony.Application.Configuration;

namespace Symphony.Infrastructure.Configuration;

public sealed class WorkflowOptionsProvider : IWorkflowOptionsProvider
{
    private readonly IWorkflowLoader _workflowLoader;
    private readonly IWorkflowOptionsResolver _workflowOptionsResolver;

    public WorkflowOptionsProvider(
        IWorkflowLoader workflowLoader,
        IWorkflowOptionsResolver workflowOptionsResolver)
    {
        _workflowLoader = workflowLoader;
        _workflowOptionsResolver = workflowOptionsResolver;
    }

    public async Task<WorkflowServiceOptions> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var workflowPath = Path.Combine(Directory.GetCurrentDirectory(), "WORKFLOW.md");
        var workflowDefinition = await _workflowLoader.LoadAsync(workflowPath, cancellationToken).ConfigureAwait(false);

        return _workflowOptionsResolver.Resolve(workflowDefinition);
    }
}
