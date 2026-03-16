using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Symphony.Abstractions.Workflows;
using Symphony.Application.Configuration;

namespace Symphony.Infrastructure.Workflows;

public sealed class WorkflowStartupValidationHostedService : IHostedService
{
    private readonly ILogger<WorkflowStartupValidationHostedService> _logger;
    private readonly IWorkflowOptionsProvider _workflowOptionsProvider;

    public WorkflowStartupValidationHostedService(
        IWorkflowOptionsProvider workflowOptionsProvider,
        ILogger<WorkflowStartupValidationHostedService> logger)
    {
        _workflowOptionsProvider = workflowOptionsProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var workflowPath = Path.Combine(Directory.GetCurrentDirectory(), "WORKFLOW.md");

        try
        {
            await _workflowOptionsProvider.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (WorkflowLoadException exception)
        {
            throw CreateStartupValidationFailure(exception, exception.Code, workflowPath);
        }
        catch (WorkflowConfigurationException exception)
        {
            throw CreateStartupValidationFailure(exception, exception.Code, workflowPath);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private InvalidOperationException CreateStartupValidationFailure(
        Exception exception,
        string errorCode,
        string workflowPath)
    {
        _logger.LogError(
            exception,
            "Failed to validate workflow file '{WorkflowPath}' ({ErrorCode}). Fix WORKFLOW.md before starting Symphony.",
            workflowPath,
            errorCode);

        return new InvalidOperationException(
            $"Failed to validate workflow file '{workflowPath}' ({errorCode}). {exception.Message}",
            exception);
    }
}
