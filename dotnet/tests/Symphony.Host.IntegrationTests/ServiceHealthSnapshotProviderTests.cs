using Symphony.Application.Configuration;
using Symphony.Application.Polling;
using Symphony.Host.Health;

namespace Symphony.Host.IntegrationTests;

public sealed class ServiceHealthSnapshotProviderTests
{
    [Fact]
    public void GetSnapshot_returns_degraded_when_last_successful_poll_is_stale()
    {
        var pollingStatusTracker = new PollingStatusTracker();
        pollingStatusTracker.RecordStarted(new DateTimeOffset(2026, 3, 18, 14, 59, 0, TimeSpan.Zero));
        pollingStatusTracker.RecordCompleted(new DateTimeOffset(2026, 3, 18, 14, 59, 0, TimeSpan.Zero));
        var provider = new ServiceHealthSnapshotProvider(
            pollingStatusTracker,
            new StaticWorkflowLoadStatusReader(
                new WorkflowLoadStatusSnapshot(
                    "Loaded",
                    "C:\\repo\\WORKFLOW.md",
                    new DateTimeOffset(2026, 3, 18, 14, 58, 0, TimeSpan.Zero),
                    null,
                    null,
                    null,
                    1_000)),
            new FakeTimeProvider(new DateTimeOffset(2026, 3, 18, 15, 0, 0, TimeSpan.Zero)));

        var snapshot = provider.GetSnapshot();

        Assert.Equal("Degraded", snapshot.Status);
        Assert.True(snapshot.PollIsStale);
        Assert.Equal(60d, snapshot.LastSuccessfulPollAgeSeconds);
    }

    [Fact]
    public void GetSnapshot_returns_degraded_when_workflow_reload_failed_using_last_known_good()
    {
        var pollingStatusTracker = new PollingStatusTracker();
        pollingStatusTracker.RecordStarted(new DateTimeOffset(2026, 3, 18, 14, 59, 55, TimeSpan.Zero));
        pollingStatusTracker.RecordCompleted(new DateTimeOffset(2026, 3, 18, 14, 59, 56, TimeSpan.Zero));
        var provider = new ServiceHealthSnapshotProvider(
            pollingStatusTracker,
            new StaticWorkflowLoadStatusReader(
                new WorkflowLoadStatusSnapshot(
                    "ReloadFailedUsingLastKnownGood",
                    "C:\\repo\\WORKFLOW.md",
                    new DateTimeOffset(2026, 3, 18, 14, 58, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 3, 18, 14, 59, 57, TimeSpan.Zero),
                    "workflow_parse_error",
                    "Front matter is invalid.",
                    30_000)),
            new FakeTimeProvider(new DateTimeOffset(2026, 3, 18, 15, 0, 0, TimeSpan.Zero)));

        var snapshot = provider.GetSnapshot();

        Assert.Equal("Degraded", snapshot.Status);
        Assert.False(snapshot.PollIsStale);
        Assert.Equal("ReloadFailedUsingLastKnownGood", snapshot.WorkflowLoadStatus);
        Assert.Equal("Front matter is invalid.", snapshot.WorkflowLastError);
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
