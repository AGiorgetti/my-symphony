using Symphony.Application.Configuration;
using Symphony.Application.Polling;
using Symphony.Application.Runtime;
using Symphony.Abstractions.Orchestration;
using Symphony.Host.Dashboard;
using Symphony.Host.Health;
using Microsoft.Extensions.Logging.Abstractions;

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
                new StaticOrchestratorControlStatusReader(OrchestratorControlState.Started),
                new StaticWorkflowLoadStatusReader(
                    new WorkflowLoadStatusSnapshot(
                        "Loaded",
                        "C:\\repo\\WORKFLOW.md",
                        new DateTimeOffset(2026, 3, 16, 14, 58, 0, TimeSpan.Zero),
                        null,
                        null,
                        null,
                        30_000)),
                new FakeTimeProvider(new DateTimeOffset(2026, 3, 16, 15, 0, 5, TimeSpan.Zero))),
            new SessionActivityStore(NullLogger<SessionActivityStore>.Instance));

        var snapshot = await service.GetSnapshotAsync();

        Assert.Equal("Healthy", snapshot.ServiceHealth);
        Assert.Equal("Single-process in-memory", snapshot.OrchestratorMode);
        Assert.Equal(OrchestratorControlState.Started, snapshot.OrchestratorState);
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
                new StaticOrchestratorControlStatusReader(OrchestratorControlState.Started),
                new StaticWorkflowLoadStatusReader(
                    new WorkflowLoadStatusSnapshot(
                        "ReloadFailedUsingLastKnownGood",
                        "C:\\repo\\WORKFLOW.md",
                        new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero),
                        new DateTimeOffset(2026, 3, 16, 15, 10, 30, TimeSpan.Zero),
                        "workflow_parse_error",
                        "Tracker config is invalid.",
                        30_000)),
                new FakeTimeProvider(new DateTimeOffset(2026, 3, 16, 15, 11, 5, TimeSpan.Zero))),
            new SessionActivityStore(NullLogger<SessionActivityStore>.Instance));

        var snapshot = await service.GetSnapshotAsync();

        Assert.Equal("Degraded", snapshot.ServiceHealth);
        Assert.Equal(new DateTimeOffset(2026, 3, 16, 15, 11, 0, TimeSpan.Zero), snapshot.LastPollTickAt);
        Assert.Equal("Tracker request failed", snapshot.LastError);
        Assert.Equal("ReloadFailedUsingLastKnownGood", snapshot.WorkflowLoadStatus);
        Assert.Equal("Tracker config is invalid.", snapshot.WorkflowLastError);
    }

    [Fact]
    public async Task GetSnapshotAsync_records_session_activity_transitions_between_snapshots()
    {
        var runtimeService = new StubRuntimeService
        {
            StateSnapshot = new OrchestratorStateSnapshot(
                new DateTimeOffset(2026, 3, 16, 16, 0, 0, TimeSpan.Zero),
                [
                    new RunningIssueSnapshot(
                        "1",
                        "ABC-1",
                        "In Progress",
                        "thread-1-turn-1",
                        1,
                        "session_started",
                        "Started session",
                        new DateTimeOffset(2026, 3, 16, 15, 58, 0, TimeSpan.Zero),
                        new DateTimeOffset(2026, 3, 16, 15, 58, 10, TimeSpan.Zero),
                        10,
                        4,
                        14),
                    new RunningIssueSnapshot(
                        "3",
                        "ABC-3",
                        "Streaming",
                        "thread-3-turn-1",
                        1,
                        "message_delta",
                        "Working",
                        new DateTimeOffset(2026, 3, 16, 15, 57, 0, TimeSpan.Zero),
                        new DateTimeOffset(2026, 3, 16, 15, 59, 0, TimeSpan.Zero),
                        20,
                        10,
                        30)
                ],
                Array.Empty<Symphony.Abstractions.Orchestration.RetryDispatchSnapshot>(),
                new CodexTotalsSnapshot(30, 14, 44, 120d),
                RateLimits: null)
        };
        var attemptHistoryTracker = new AttemptHistoryTracker();
        var pollingStatusTracker = new PollingStatusTracker();
        pollingStatusTracker.RecordCompleted(new DateTimeOffset(2026, 3, 16, 16, 0, 0, TimeSpan.Zero));
        var sessionActivityStore = new SessionActivityStore(NullLogger<SessionActivityStore>.Instance);
        var service = new DashboardStateService(
            runtimeService,
            attemptHistoryTracker,
            new ServiceHealthSnapshotProvider(
                pollingStatusTracker,
                new StaticOrchestratorControlStatusReader(OrchestratorControlState.Started),
                new StaticWorkflowLoadStatusReader(
                    new WorkflowLoadStatusSnapshot(
                        "Loaded",
                        "C:\\repo\\WORKFLOW.md",
                        new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero),
                        null,
                        null,
                        null,
                        30_000)),
                new FakeTimeProvider(new DateTimeOffset(2026, 3, 16, 16, 0, 5, TimeSpan.Zero))),
            sessionActivityStore);

        await service.GetSnapshotAsync();

        runtimeService.StateSnapshot = new OrchestratorStateSnapshot(
            new DateTimeOffset(2026, 3, 16, 16, 5, 0, TimeSpan.Zero),
            [
                new RunningIssueSnapshot(
                    "1",
                    "ABC-1",
                    "Finishing",
                    "thread-1-turn-2",
                    2,
                    "turn_completed",
                    "Applied changes",
                    new DateTimeOffset(2026, 3, 16, 15, 58, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 3, 16, 16, 4, 30, TimeSpan.Zero),
                    40,
                    15,
                    55),
                new RunningIssueSnapshot(
                    "2",
                    "ABC-2",
                    "In Progress",
                    "thread-2-turn-1",
                    1,
                    "session_started",
                    "Bootstrapped branch",
                    new DateTimeOffset(2026, 3, 16, 16, 4, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 3, 16, 16, 4, 10, TimeSpan.Zero),
                    18,
                    7,
                    25)
            ],
            [
                new Symphony.Abstractions.Orchestration.RetryDispatchSnapshot(
                    "1",
                    "ABC-1",
                    2,
                    new DateTimeOffset(2026, 3, 16, 16, 6, 0, TimeSpan.Zero),
                    "retry later")
            ],
            new CodexTotalsSnapshot(58, 22, 80, 180d),
            RateLimits: null);
        attemptHistoryTracker.Record(
            "3",
            "ABC-3",
            2,
            "Succeeded",
            new DateTimeOffset(2026, 3, 16, 15, 57, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 16, 16, 4, 0, TimeSpan.Zero),
            null,
            "thread-3-turn-1");
        attemptHistoryTracker.Record(
            "4",
            "ABC-4",
            1,
            "Failed",
            new DateTimeOffset(2026, 3, 16, 16, 1, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 16, 16, 4, 30, TimeSpan.Zero),
            "Tracker request failed",
            "thread-4-turn-1");

        await service.GetSnapshotAsync();

        var abc1Activities = sessionActivityStore.GetActivities("ABC-1");
        Assert.Contains(abc1Activities, activity => activity.Kind == SessionActivityKind.LifecycleMilestone && activity.Title == "Finishing");
        Assert.Contains(abc1Activities, activity => activity.Kind == SessionActivityKind.ProgressUpdate && activity.Title == "Turn 2");
        Assert.Contains(abc1Activities, activity => activity.Kind == SessionActivityKind.AgentMessage && activity.Title == "turn_completed" && activity.Detail == "Applied changes");
        Assert.Contains(abc1Activities, activity => activity.Kind == SessionActivityKind.Warning && activity.Title == "Queued for retry");

        var abc2Session = sessionActivityStore.GetSession("ABC-2");
        Assert.NotNull(abc2Session);
        Assert.True(abc2Session.IsActive);
        Assert.Contains(
            sessionActivityStore.GetActivities("ABC-2"),
            activity => activity.Kind == SessionActivityKind.LifecycleMilestone && activity.Title == "Session started");

        var abc3Session = sessionActivityStore.GetSession("ABC-3");
        Assert.NotNull(abc3Session);
        Assert.False(abc3Session.IsActive);
        Assert.Equal("Succeeded", abc3Session.FinalOutcome);
        Assert.Contains(
            sessionActivityStore.GetActivities("ABC-3"),
            activity => activity.Kind == SessionActivityKind.Outcome && activity.Title == "Succeeded");

        var abc4Session = sessionActivityStore.GetSession("ABC-4");
        Assert.NotNull(abc4Session);
        Assert.False(abc4Session.IsActive);
        Assert.Equal("Failed", abc4Session.FinalOutcome);
        Assert.Equal("Tracker request failed", abc4Session.FinalError);
        Assert.Contains(
            sessionActivityStore.GetActivities("ABC-4"),
            activity => activity.Kind == SessionActivityKind.Outcome && activity.Title == "Failed" && activity.Detail == "Tracker request failed");
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

    private sealed class StaticOrchestratorControlStatusReader(OrchestratorControlState state) : IOrchestratorControlStatusReader
    {
        public OrchestratorControlSnapshot GetSnapshot()
        {
            return new OrchestratorControlSnapshot(state, new DateTimeOffset(2026, 3, 16, 14, 58, 0, TimeSpan.Zero));
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
