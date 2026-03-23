using Bunit;
using Flowbite.Components;
using Flowbite.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Symphony.Abstractions.Orchestration;
using Symphony.Application.Polling;
using Symphony.Application.Runtime;
using Symphony.Host.Components.Pages;
using Symphony.Host.Components.SessionDetail;
using Symphony.Host.Dashboard;

namespace Symphony.Host.IntegrationTests;

public sealed class SessionDetailComponentTests : BunitContext
{
    public SessionDetailComponentTests()
    {
        Services.AddFlowbite();
    }

    [Fact]
    public void SessionHeaderCard_renders_status_tracker_link_and_spinner_for_active_sessions()
    {
        var cut = Render<SessionHeaderCard>(
            parameters => parameters.Add(
                component => component.Session,
                new SessionHeaderDisplayModel(
                    "ABC-1",
                    "https://github.com/AGiorgetti/my-symphony/issues/ABC-1",
                    true,
                    "Active",
                    Status: null,
                    Badge.BadgeColor.Info,
                    new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero),
                    EndedAt: null)));

        Assert.Contains("ABC-1", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Open tracker issue", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Session is active", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Still running", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionActivityTimeline_renders_warning_and_failure_alerts()
    {
        var cut = Render<SessionActivityTimeline>(
            parameters => parameters.Add(
                component => component.Timeline,
                new SessionActivityTimelineModel(
                    [
                        new SessionActivityTimelineEntryModel(
                            SessionActivityKind.LifecycleMilestone,
                            new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero),
                            "2026-03-20 09:00:00 UTC",
                            "Session started",
                            "Lifecycle",
                            Badge.BadgeColor.Gray,
                            null,
                            Array.Empty<SessionActivityFactModel>(),
                            "Tracker moved to In Progress",
                            null,
                            null,
                            false,
                            false,
                            TimelineColor.Gray),
                        new SessionActivityTimelineEntryModel(
                            SessionActivityKind.AgentMessage,
                            new DateTimeOffset(2026, 3, 20, 9, 0, 30, TimeSpan.Zero),
                            "2026-03-20 09:00:30 UTC",
                            "turn_started",
                            "Agent message",
                            Badge.BadgeColor.Info,
                            null,
                            Array.Empty<SessionActivityFactModel>(),
                            "Agent picked up the first turn",
                            null,
                            null,
                            false,
                            false,
                            TimelineColor.Blue),
                        new SessionActivityTimelineEntryModel(
                            SessionActivityKind.Warning,
                            new DateTimeOffset(2026, 3, 20, 9, 1, 0, TimeSpan.Zero),
                            "2026-03-20 09:01:00 UTC",
                            "Queued for retry",
                            "Warning",
                            Badge.BadgeColor.Warning,
                            null,
                            Array.Empty<SessionActivityFactModel>(),
                            "Waiting for the next dispatcher slot",
                            null,
                            null,
                            false,
                            false,
                            TimelineColor.Orange)
                    ],
                    new SessionActivityTimelineAlertModel(AlertColor.Warning, "Latest warning:", "Queued for retry - Waiting for the next dispatcher slot"),
                    new SessionActivityTimelineAlertModel(AlertColor.Failure, "Failure detail:", "Prompt build failed"))));

        Assert.Contains("data-testid=\"session-detail-latest-attention-alert\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-detail-failure-alert\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Session started", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Queued for retry", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("border-amber-400/30", cut.Markup, StringComparison.Ordinal);

        var timelineItems = cut.FindComponents<TimelineItem>();

        Assert.Collection(
            timelineItems,
            item =>
            {
                Assert.Equal("Session started", item.Instance.Title);
                Assert.Equal(TimelineColor.Gray, item.Instance.Color);
            },
            item =>
            {
                Assert.Equal("turn_started", item.Instance.Title);
                Assert.Equal(TimelineColor.Blue, item.Instance.Color);
            },
            item =>
            {
                Assert.Equal("Queued for retry", item.Instance.Title);
                Assert.Equal(TimelineColor.Orange, item.Instance.Color);
            });
    }

    [Fact]
    public void SessionActivityTimeline_renders_expandable_json_payloads_compactly()
    {
        var cut = Render<SessionActivityTimeline>(
            parameters => parameters.Add(
                component => component.Timeline,
                new SessionActivityTimelineModel(
                    [
                        new SessionActivityTimelineEntryModel(
                            SessionActivityKind.AgentMessage,
                            new DateTimeOffset(2026, 3, 20, 9, 2, 0, TimeSpan.Zero),
                            "2026-03-20 09:02:00 UTC",
                            "turn_completed",
                            "Agent message",
                            Badge.BadgeColor.Info,
                            "Event: turn_completed | Files: Program.cs | Input: 12",
                            [
                                new SessionActivityFactModel("Event", "turn_completed"),
                                new SessionActivityFactModel("Files", "Program.cs"),
                                new SessionActivityFactModel("Input", "12")
                            ],
                            "{\n  \"event\": \"turn_completed\",\n  \"files\": [\"Program.cs\"],\n  \"stats\": { \"input\": 12 }\n}",
                            "Event: turn_completed | Files: Program.cs | Input: 12",
                            "View structured payload",
                            true,
                            true,
                            TimelineColor.Blue)
                    ],
                    LatestAttentionAlert: null,
                    FailureAlert: null)));

        var details = cut.Find("[data-testid=\"session-detail-timeline-detail\"]");

        Assert.Contains("Event: turn_completed", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Files: Program.cs", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("View structured payload", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("\"turn_completed\"", details.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionMetadataPanel_renders_metadata_fields_and_attempt_label()
    {
        var cut = Render<SessionMetadataPanel>(
            parameters => parameters.Add(
                component => component.Metadata,
                new SessionMetadataPanelModel(
                    120,
                    45,
                    165,
                    4,
                    "thread-1-turn-4",
                    2,
                    true,
                    "Finished sessions keep the last known session ID and attempt when available.")));

        Assert.Contains("165", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("thread-1-turn-4", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Attempt 2", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Finished sessions keep the last known session ID and attempt when available.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionDetailPage_starts_refresh_loop_for_active_sessions_only()
    {
        var startedAt = new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero);
        var store = new SessionActivityStore(NullLogger<SessionActivityStore>.Instance);
        store.RecordSessionStart("ABC-1", startedAt, "https://github.com/AGiorgetti/my-symphony/issues/ABC-1");
        store.RecordActivity("ABC-1", new SessionActivityEntry(SessionActivityKind.LifecycleMilestone, startedAt, "Session started", "Tracker moved to In Progress"));

        var dashboardStateService = new CountingDashboardStateService(
            new DashboardSnapshot(
                startedAt.AddMinutes(1),
                "Healthy",
                "Single-process in-memory",
                OrchestratorControlState.Started,
                startedAt.AddMinutes(1),
                startedAt.AddMinutes(1),
                startedAt.AddMinutes(1),
                5d,
                "Loaded",
                startedAt,
                RunningCount: 1,
                RetryingCount: 0,
                InputTokens: 120,
                OutputTokens: 45,
                TotalTokens: 165,
                SecondsRunning: 60d,
                [
                    new DashboardActiveSessionSnapshot(
                        "ABC-1",
                        "StreamingTurn",
                        "thread-1-turn-4",
                        4,
                        "turn_completed",
                        "Applied changes",
                        startedAt,
                        startedAt.AddMinutes(1),
                        165)
                ],
                RetryQueue: [],
                RecentAttempts: [],
                LastError: null,
                WorkflowLastError: null));
        var runtimeService = new StaticRuntimeService(
            new OrchestratorIssueSnapshot(
                "ABC-1",
                "1",
                "running",
                RestartCount: 1,
                CurrentRetryAttempt: 2,
                new RunningIssueSnapshot(
                    "1",
                    "ABC-1",
                    "StreamingTurn",
                    "thread-1-turn-4",
                    4,
                    "turn_completed",
                    "Applied changes",
                    startedAt,
                    startedAt.AddMinutes(1),
                    120,
                    45,
                    165),
                Retry: null,
                LastError: null,
                RecentEvents: []));

        Services.AddSingleton<ISessionActivityStore>(store);
        Services.AddSingleton<IDashboardStateService>(dashboardStateService);
        Services.AddSingleton<IOrchestratorRuntimeService>(runtimeService);

        using var cut = Render<SessionDetailPage>(
            parameters => parameters.Add(component => component.Identifier, "ABC-1"));

        cut.WaitForAssertion(
            () => Assert.True(dashboardStateService.CallCount >= 2),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SessionDetailPage_skips_refresh_loop_for_ended_sessions()
    {
        var startedAt = new DateTimeOffset(2026, 3, 20, 8, 0, 0, TimeSpan.Zero);
        var store = new SessionActivityStore(NullLogger<SessionActivityStore>.Instance);
        store.RecordSessionStart("ABC-2", startedAt, "https://github.com/AGiorgetti/my-symphony/issues/ABC-2");
        store.RecordSessionEnd("ABC-2", startedAt.AddMinutes(4), "Succeeded");

        var dashboardStateService = new CountingDashboardStateService(
            new DashboardSnapshot(
                startedAt.AddMinutes(5),
                "Healthy",
                "Single-process in-memory",
                OrchestratorControlState.Started,
                startedAt.AddMinutes(5),
                startedAt.AddMinutes(5),
                startedAt.AddMinutes(5),
                5d,
                "Loaded",
                startedAt,
                RunningCount: 0,
                RetryingCount: 0,
                InputTokens: 0,
                OutputTokens: 0,
                TotalTokens: 0,
                SecondsRunning: 0d,
                ActiveSessions: [],
                RetryQueue: [],
                RecentAttempts:
                [
                    new DashboardRecentAttemptSnapshot(
                        "ABC-2",
                        1,
                        "Succeeded",
                        startedAt.AddMinutes(4),
                        240d,
                        null,
                        "thread-2-turn-3")
                ],
                LastError: null,
                WorkflowLastError: null));

        Services.AddSingleton<ISessionActivityStore>(store);
        Services.AddSingleton<IDashboardStateService>(dashboardStateService);
        Services.AddSingleton<IOrchestratorRuntimeService>(new StaticRuntimeService(issueSnapshot: null));

        using var cut = Render<SessionDetailPage>(
            parameters => parameters.Add(component => component.Identifier, "ABC-2"));

        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.Equal(1, dashboardStateService.CallCount);
        Assert.Contains("data-testid=\"session-detail-metadata\"", cut.Markup, StringComparison.Ordinal);
    }

    private sealed class CountingDashboardStateService(DashboardSnapshot snapshot) : IDashboardStateService
    {
        public int CallCount { get; private set; }

        public Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class StaticRuntimeService(OrchestratorIssueSnapshot? issueSnapshot) : IOrchestratorRuntimeService
    {
        public Task<OrchestratorStateSnapshot> GetStateSnapshotAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new OrchestratorStateSnapshot(
                    DateTimeOffset.UtcNow,
                    Array.Empty<RunningIssueSnapshot>(),
                    Array.Empty<Symphony.Abstractions.Orchestration.RetryDispatchSnapshot>(),
                    new CodexTotalsSnapshot(0, 0, 0, 0d),
                    RateLimits: null));
        }

        public Task<OrchestratorIssueSnapshot?> GetIssueSnapshotAsync(string issueIdentifier, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(issueSnapshot);
        }

        public PollingRefreshReceipt RequestRefresh()
        {
            return new PollingRefreshReceipt(true, false, DateTimeOffset.UtcNow, ["poll", "reconcile"]);
        }
    }
}
