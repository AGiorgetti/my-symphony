using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Symphony.Application.Configuration;

namespace Symphony.Application.Polling;

public sealed class PollingBackgroundService : BackgroundService
{
    private readonly IPollingIterationHandler _pollingIterationHandler;
    private readonly ILogger<PollingBackgroundService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IWorkflowOptionsProvider _workflowOptionsProvider;

    public PollingBackgroundService(
        IWorkflowOptionsProvider workflowOptionsProvider,
        IPollingIterationHandler pollingIterationHandler,
        TimeProvider timeProvider,
        ILogger<PollingBackgroundService> logger)
    {
        _workflowOptionsProvider = workflowOptionsProvider;
        _pollingIterationHandler = pollingIterationHandler;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var currentOptions = await _workflowOptionsProvider.GetCurrentAsync(stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _pollingIterationHandler.ExecuteAsync(currentOptions, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Polling iteration failed. Continuing after {PollingIntervalMs}ms.",
                    currentOptions.Polling.IntervalMs);
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            currentOptions = await TryRefreshWorkflowOptionsAsync(currentOptions, stoppingToken).ConfigureAwait(false);

            try
            {
                await Task.Delay(
                        TimeSpan.FromMilliseconds(currentOptions.Polling.IntervalMs),
                        _timeProvider,
                        stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task<WorkflowServiceOptions> TryRefreshWorkflowOptionsAsync(
        WorkflowServiceOptions currentOptions,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _workflowOptionsProvider.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to re-apply workflow configuration. Continuing with previous polling interval {PollingIntervalMs}ms.",
                currentOptions.Polling.IntervalMs);

            return currentOptions;
        }
    }
}
