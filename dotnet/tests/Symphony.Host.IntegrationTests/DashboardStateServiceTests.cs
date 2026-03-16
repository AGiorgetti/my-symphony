using Symphony.Application.Polling;
using Symphony.Application.Runtime;
using Symphony.Host.Dashboard;

namespace Symphony.Host.IntegrationTests;

public sealed class DashboardStateServiceTests
{
    [Fact]
    public async Task GetSnapshotAsync_returns_healthy_dashboard_summary_after_successful_poll()
    {
        var runtimeService = new StubRuntimeService
        {
            StateSnapshot = new OrchestratorStateSnapshot(
                new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero),
                [],
                [],
                new CodexTotalsSnapshot(120, 45, 165, 90d),
                RateLimits: null)
        };
        var pollingStatusTracker = new PollingStatusTracker();
        pollingStatusTracker.RecordStarted(new DateTimeOffset(2026, 3, 16, 14, 59, 0, TimeSpan.Zero));
        pollingStatusTracker.RecordCompleted(new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero));
        var service = new DashboardStateService(runtimeService, pollingStatusTracker);

        var snapshot = await service.GetSnapshotAsync();

        Assert.Equal("Healthy", snapshot.ServiceHealth);
        Assert.Equal("Single-process in-memory", snapshot.OrchestratorMode);
        Assert.Equal(new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero), snapshot.LastPollTickAt);
        Assert.Equal(165, snapshot.TotalTokens);
        Assert.Null(snapshot.LastError);
    }

    [Fact]
    public async Task GetSnapshotAsync_returns_degraded_dashboard_summary_after_failed_poll()
    {
        var runtimeService = new StubRuntimeService();
        var pollingStatusTracker = new PollingStatusTracker();
        pollingStatusTracker.RecordStarted(new DateTimeOffset(2026, 3, 16, 15, 10, 0, TimeSpan.Zero));
        pollingStatusTracker.RecordFailed(new DateTimeOffset(2026, 3, 16, 15, 11, 0, TimeSpan.Zero), "Tracker request failed");
        var service = new DashboardStateService(runtimeService, pollingStatusTracker);

        var snapshot = await service.GetSnapshotAsync();

        Assert.Equal("Degraded", snapshot.ServiceHealth);
        Assert.Equal(new DateTimeOffset(2026, 3, 16, 15, 11, 0, TimeSpan.Zero), snapshot.LastPollTickAt);
        Assert.Equal("Tracker request failed", snapshot.LastError);
    }

    private sealed class StubRuntimeService : IOrchestratorRuntimeService
    {
        public OrchestratorStateSnapshot StateSnapshot { get; set; } = new(
            DateTimeOffset.UtcNow,
            Array.Empty<RunningIssueSnapshot>(),
            Array.Empty<Symphony.Abstractions.Orchestration.RetryDispatchSnapshot>(),
            new CodexTotalsSnapshot(0, 0, 0, 0d),
            RateLimits: null);

        public Task<OrchestratorStateSnapshot> GetStateSnapshotAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(StateSnapshot);
        }

        public Task<OrchestratorIssueSnapshot?> GetIssueSnapshotAsync(string issueIdentifier, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<OrchestratorIssueSnapshot?>(null);
        }

        public PollingRefreshReceipt RequestRefresh()
        {
            return new PollingRefreshReceipt(true, false, DateTimeOffset.UtcNow, ["poll", "reconcile"]);
        }
    }
}
