using Symphony.Application.Orchestration;

namespace Symphony.Application.Tests.Orchestration;

public sealed class RetryDelayPlannerTests
{
    [Fact]
    public void GetContinuationDelay_returns_one_second()
    {
        var planner = new RetryDelayPlanner(() => 1d);

        var delay = planner.GetContinuationDelay();

        Assert.Equal(TimeSpan.FromSeconds(1), delay);
    }

    [Theory]
    [InlineData(1, 300_000, 0d, 5_000)]
    [InlineData(1, 300_000, 1d, 10_000)]
    [InlineData(3, 300_000, 1d, 40_000)]
    [InlineData(10, 25_000, 1d, 25_000)]
    public async Task GetFailureDelayAsync_applies_exponential_backoff_with_cap_and_jitter(
        int attempt,
        int maxRetryBackoffMs,
        double jitterSample,
        int expectedDelayMs)
    {
        var planner = new RetryDelayPlanner(() => jitterSample);

        var delay = await planner.GetFailureDelayAsync(attempt, maxRetryBackoffMs);

        Assert.Equal(TimeSpan.FromMilliseconds(expectedDelayMs), delay);
    }
}
