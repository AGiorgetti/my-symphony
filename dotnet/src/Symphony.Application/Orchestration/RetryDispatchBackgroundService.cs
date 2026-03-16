using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Symphony.Application.Orchestration;

public sealed class RetryDispatchBackgroundService(
    OrchestratorDispatchQueue dispatchQueue,
    TimeProvider timeProvider,
    ILogger<RetryDispatchBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await dispatchQueue.ProcessDueRetriesAsync(stoppingToken).ConfigureAwait(false);

                var delay = dispatchQueue.GetTimeUntilNextRetry();
                if (delay is null)
                {
                    await dispatchQueue.WaitForRetrySignalAsync(stoppingToken).ConfigureAwait(false);
                    continue;
                }

                if (delay <= TimeSpan.Zero)
                {
                    continue;
                }

                var signalTask = dispatchQueue.WaitForRetrySignalAsync(stoppingToken);
                var delayTask = Task.Delay(delay.Value, timeProvider, stoppingToken);
                await Task.WhenAny(signalTask, delayTask).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("Retry dispatch background service stopped during shutdown.");
        }
    }
}
