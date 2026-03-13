using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Symphony.Abstractions.Workflows;

namespace Symphony.Infrastructure.Workflows;

public sealed class WorkflowStartupValidationHostedService : IHostedService
{
    private readonly ILogger<WorkflowStartupValidationHostedService> _logger;
    private readonly IWorkflowLoader _workflowLoader;

    public WorkflowStartupValidationHostedService(
        IWorkflowLoader workflowLoader,
        ILogger<WorkflowStartupValidationHostedService> logger)
    {
        _workflowLoader = workflowLoader;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var workflowPath = Path.Combine(Directory.GetCurrentDirectory(), "WORKFLOW.md");

        try
        {
            await _workflowLoader.LoadAsync(workflowPath, cancellationToken).ConfigureAwait(false);
        }
        catch (WorkflowLoadException exception)
        {
            _logger.LogError(
                exception,
                "Failed to load workflow file '{WorkflowPath}' ({ErrorCode}). Fix WORKFLOW.md before starting Symphony.",
                exception.WorkflowPath,
                exception.Code);

            throw new InvalidOperationException(
                $"Failed to load workflow file '{exception.WorkflowPath}' ({exception.Code}). {exception.Message}",
                exception);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
