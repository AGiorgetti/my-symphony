using Polly;
using Polly.Retry;

namespace Symphony.Application.Orchestration;

public sealed class RetryDelayPlanner
{
    private static readonly RetryStrategyOptions DelayStrategy = new()
    {
        MaxRetryAttempts = int.MaxValue,
        Delay = TimeSpan.FromSeconds(10),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true
    };

    private readonly Func<double> _nextJitterSample;

    public RetryDelayPlanner()
        : this(() => Random.Shared.NextDouble())
    {
    }

    public RetryDelayPlanner(Func<double> nextJitterSample)
    {
        _nextJitterSample = nextJitterSample ?? throw new ArgumentNullException(nameof(nextJitterSample));
    }

    public TimeSpan GetContinuationDelay()
    {
        return TimeSpan.FromSeconds(1);
    }

    public ValueTask<TimeSpan> GetFailureDelayAsync(
        int attempt,
        int maxRetryBackoffMs,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attempt);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRetryBackoffMs);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(ComputeFailureDelay(attempt, maxRetryBackoffMs));
    }

    private TimeSpan ComputeFailureDelay(int attempt, int maxRetryBackoffMs)
    {
        var baseDelayMs = DelayStrategy.Delay.TotalMilliseconds;
        var delayMs = DelayStrategy.BackoffType switch
        {
            DelayBackoffType.Exponential => baseDelayMs * Math.Pow(2d, attempt - 1),
            DelayBackoffType.Linear => baseDelayMs * attempt,
            _ => baseDelayMs
        };

        delayMs = Math.Min(delayMs, maxRetryBackoffMs);
        if (DelayStrategy.UseJitter)
        {
            var jitterSample = Math.Clamp(_nextJitterSample(), 0d, 1d);
            delayMs *= 0.5d + (0.5d * jitterSample);
        }

        return TimeSpan.FromMilliseconds(Math.Max(1d, Math.Min(delayMs, maxRetryBackoffMs)));
    }
}
