using Symphony.Application.Configuration;
using Symphony.Application.Polling;
using Symphony.Application.Runtime;
using Symphony.Host.Dashboard;
using Symphony.Host.Health;

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
                [
                    new RunningIssueSnapshot(
                        "1",
                        "ABC-1",
                        "In Progress",
                        "thread-1-turn-1",
                        4,
                        "turn_completed",
                        "Applied changes",
                        new DateTimeOffset(2026, 3, 16, 14, 55, 0, TimeSpan.Zero),
                        new DateTimeOffset(2026, 3, 16, 14, 59, 30, TimeSpan.Zero),
                        120,
                        45,
                        165)
                ],
                [
                    new Symphony.Abstractions.Orchestration.RetryDispatchSnapshot(
                        "2",
                        "ABC-2",
                        2,
                        new DateTimeOffset(2026, 3, 16, 15, 2, 0, TimeSpan.Zero),
                        "retry later")
                ],
                new CodexTotalsSnapshot(120, 45, 165, 90d),
                RateLimits: null)
        };
        var attemptHistoryTracker = new AttemptHistoryTracker();
        attemptHistoryTracker.Record(
            "3",
            "ABC-3",
            1,
            "Retrying",
            new DateTimeOffset(2026, 3, 16, 14, 58, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 16, 14, 59, 0, TimeSpan.Zero),
            "Tracker request failed",
            "thread-3-turn-2");
        var pollingStatusTracker = new PollingStatusTracker();
        pollingStatusTracker.RecordStarted(new DateTimeOffset(2026, 3, 16, 14, 59, 0, TimeSpan.Zero));
        pollingStatusTracker.RecordCompleted(new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero));
        var service = new DashboardStateService(
            runtimeService,
            attemptHistoryTracker,
            new ServiceHealthSnapshotProvider(
                pollingStatusTracker,
                new StaticWorkflowLoadStatusReader(
                    new WorkflowLoadStatusSnapshot(
                        "Loaded",
                        "C:\\repo\\WORKFLOW.md",
                        new DateTimeOffset(2026, 3, 16, 14, 58, 0, TimeSpan.Zero),
                        null,
                        null,
                        null,
                        30_000)),
                new FakeTimeProvider(new DateTimeOffset(2026, 3, 16, 15, 0, 5, TimeSpan.Zero))));

        var snapshot = await service.GetSnapshotAsync();

        Assert.Equal("Healthy", snapshot.ServiceHealth);
        Assert.Equal("Single-process in-memory", snapshot.OrchestratorMode);
        Assert.Equal(new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero), snapshot.LastPollTickAt);
        Assert.Equal(new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero), snapshot.LastSuccessfulPollAt);
        Assert.Equal(5d, snapshot.LastSuccessfulPollAgeSeconds);
        Assert.Equal("Loaded", snapshot.WorkflowLoadStatus);
        Assert.Single(snapshot.ActiveSessions);
        Assert.Single(snapshot.RetryQueue);
        Assert.Single(snapshot.RecentAttempts);
        Assert.Equal("ABC-1", snapshot.ActiveSessions[0].IssueIdentifier);
        Assert.Equal("ABC-2", snapshot.RetryQueue[0].IssueIdentifier);
        Assert.Equal("Retrying", snapshot.RecentAttempts[0].Outcome);
        Assert.Equal(165, snapshot.TotalTokens);
        Assert.Null(snapshot.LastError);
    }

    [Fact]
    public async Task GetSnapshotAsync_returns_degraded_dashboard_summary_after_failed_poll()
    {
        var runtimeService = new StubRuntimeService();
        var attemptHistoryTracker = new AttemptHistoryTracker();
        var pollingStatusTracker = new PollingStatusTracker();
        pollingStatusTracker.RecordStarted(new DateTimeOffset(2026, 3, 16, 15, 10, 0, TimeSpan.Zero));
        pollingStatusTracker.RecordFailed(new DateTimeOffset(2026, 3, 16, 15, 11, 0, TimeSpan.Zero), "Tracker request failed");
        var service = new DashboardStateService(
            runtimeService,
            attemptHistoryTracker,
            new ServiceHealthSnapshotProvider(
                pollingStatusTracker,
                new StaticWorkflowLoadStatusReader(
                    new WorkflowLoadStatusSnapshot(
                        "ReloadFailedUsingLastKnownGood",
                        "C:\\repo\\WORKFLOW.md",
                        new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero),
                        new DateTimeOffset(2026, 3, 16, 15, 10, 30, TimeSpan.Zero),
                        "workflow_parse_error",
                        "Tracker config is invalid.",
                        30_000)),
                new FakeTimeProvider(new DateTimeOffset(2026, 3, 16, 15, 11, 5, TimeSpan.Zero))));

        var snapshot = await service.GetSnapshotAsync();

        Assert.Equal("Degraded", snapshot.ServiceHealth);
        Assert.Equal(new DateTimeOffset(2026, 3, 16, 15, 11, 0, TimeSpan.Zero), snapshot.LastPollTickAt);
        Assert.Equal("Tracker request failed", snapshot.LastError);
        Assert.Equal("ReloadFailedUsingLastKnownGood", snapshot.WorkflowLoadStatus);
        Assert.Equal("Tracker config is invalid.", snapshot.WorkflowLastError);
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

    private sealed class StaticWorkflowLoadStatusReader(WorkflowLoadStatusSnapshot snapshot) : IWorkflowLoadStatusReader
    {
        public WorkflowLoadStatusSnapshot GetSnapshot()
        {
            return snapshot;
        }
    }

    private sealed class FakeTimeProvider(DateTimeOffset currentTime) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return currentTime;
        }
    }
}
