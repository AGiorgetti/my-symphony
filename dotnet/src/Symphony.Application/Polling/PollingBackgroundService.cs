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
        _logger.LogInformation(
            "polling_service started poll_interval_ms={poll_interval_ms} outcome=started",
            currentOptions.Polling.IntervalMs);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "poll_tick started poll_interval_ms={poll_interval_ms} outcome=started",
                    currentOptions.Polling.IntervalMs);

                try
                {
                    await _pollingIterationHandler.ExecuteAsync(currentOptions, stoppingToken).ConfigureAwait(false);
                    _logger.LogInformation(
                        "poll_tick completed poll_interval_ms={poll_interval_ms} outcome=completed",
                        currentOptions.Polling.IntervalMs);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "poll_tick failed poll_interval_ms={poll_interval_ms} outcome=failed",
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
        finally
        {
            _logger.LogInformation("polling_service completed outcome=completed");
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
                "workflow_reload failed poll_interval_ms={poll_interval_ms} outcome=failed",
                currentOptions.Polling.IntervalMs);

            return currentOptions;
        }
    }
}
