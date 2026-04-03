using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MudBlazor;
using MudBlazor.Services;
using Symphony.Abstractions.Orchestration;
using Symphony.Abstractions.Trackers;
using Symphony.Application.Configuration;
using Symphony.Application.Orchestration;
using Symphony.Application.Polling;
using Symphony.Application.Runtime;
using Symphony.Host.Components.Pages;
using Symphony.Host.Components.SessionDetail;
using Symphony.Host.Configuration;
using Symphony.Host.Dashboard;
using Symphony.Domain.Issues;
using Symphony.Domain.Sessions;

namespace Symphony.Host.IntegrationTests;

public sealed class SessionDetailComponentTests : BunitContext
{
    public SessionDetailComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IKeyInterceptorService, TestKeyInterceptorService>();
        Services.AddOptions();
        Services.Configure<DashboardUiOptions>(
            options =>
            {
                options.DebugMode = false;
                options.TrackAgentMessageDeltas = false;
            });
        Services.AddSingleton(CreateFollowUpActionResolutionService());
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
                    Color.Info,
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
                            Color.Default,
                            null,
                            Array.Empty<SessionActivityFactModel>(),
                            "Tracker moved to In Progress",
                            null,
                            null,
                            false,
                            false,
                            Color.Default),
                        new SessionActivityTimelineEntryModel(
                            SessionActivityKind.AgentMessage,
                            new DateTimeOffset(2026, 3, 20, 9, 0, 30, TimeSpan.Zero),
                            "2026-03-20 09:00:30 UTC",
                            "Turn started",
                            "Agent message",
                            Color.Info,
                            null,
                            Array.Empty<SessionActivityFactModel>(),
                            "Agent picked up the first turn",
                            null,
                            null,
                            false,
                            false,
                            Color.Info),
                        new SessionActivityTimelineEntryModel(
                            SessionActivityKind.Warning,
                            new DateTimeOffset(2026, 3, 20, 9, 1, 0, TimeSpan.Zero),
                            "2026-03-20 09:01:00 UTC",
                            "Queued for retry",
                            "Warning",
                            Color.Warning,
                            null,
                            Array.Empty<SessionActivityFactModel>(),
                            "Waiting for the next dispatcher slot",
                            null,
                            null,
                            false,
                            false,
                            Color.Warning)
                    ],
                    new SessionActivityTimelineAlertModel(Severity.Warning, "Latest warning:", "Queued for retry - Waiting for the next dispatcher slot"),
                    new SessionActivityTimelineAlertModel(Severity.Error, "Failure detail:", "Prompt build failed"))));

        Assert.Contains("data-testid=\"session-detail-latest-attention-alert\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-detail-failure-alert\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Session started", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Queued for retry", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("timeline-entry--warning", cut.Markup, StringComparison.Ordinal);

        var startedIndex = cut.Markup.IndexOf("Session started", StringComparison.Ordinal);
        var messageIndex = cut.Markup.IndexOf("Turn started", StringComparison.Ordinal);
        var warningIndex = cut.Markup.LastIndexOf("Queued for retry", StringComparison.Ordinal);

        Assert.True(startedIndex >= 0);
        Assert.True(messageIndex > startedIndex);
        Assert.True(warningIndex > messageIndex);
    }

    [Fact]
    public void SessionActivityTimeline_hides_raw_agent_payloads_when_debug_mode_is_disabled()
    {
        var cut = Render<SessionActivityTimeline>(
            parameters => parameters
                .Add(component => component.Timeline, CreateStructuredTimeline())
                .Add(component => component.DebugModeEnabled, false));

        Assert.Contains("turn_completed", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"session-detail-debug-banner\"", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"session-detail-timeline-detail\"", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Sent turn/start", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Received turn/completed", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Prompt body", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("\"message\": \"done\"", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionActivityTimeline_renders_expandable_json_payloads_when_debug_mode_is_enabled()
    {
        var cut = Render<SessionActivityTimeline>(
            parameters => parameters
                .Add(component => component.Timeline, CreateStructuredTimeline())
                .Add(component => component.DebugModeEnabled, true)
                .Add(component => component.TrackAgentMessageDeltas, true));

        Assert.Contains("data-testid=\"session-detail-debug-banner\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-detail-method-filter\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("turn/start", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("turn/completed", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("item/agentMessage/delta", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Sent turn/start", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Received turn/completed", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Received item/agentMessage/delta", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("View raw payload and debug metadata", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Debug metadata", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Raw payload", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Prompt body", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("\"message\": \"done\"", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionActivityTimeline_filters_debug_entries_by_method_checkbox()
    {
        var cut = Render<SessionActivityTimeline>(
            parameters => parameters
                .Add(component => component.Timeline, CreateStructuredTimeline())
                .Add(component => component.DebugModeEnabled, true)
                .Add(component => component.TrackAgentMessageDeltas, true));

        Assert.Contains("Received item/agentMessage/delta", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Sent turn/start", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Received turn/completed", cut.Markup, StringComparison.Ordinal);

        cut.Find("[data-testid=\"session-detail-method-filter-item-agentmessage-delta\"]").Change(false);

        Assert.DoesNotContain("Received item/agentMessage/delta", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Sent turn/start", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Received turn/completed", cut.Markup, StringComparison.Ordinal);

        cut.Find("[data-testid=\"session-detail-method-filter-turn-start\"]").Change(false);

        Assert.DoesNotContain("Sent turn/start", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Received turn/completed", cut.Markup, StringComparison.Ordinal);

        cut.Find("[data-testid=\"session-detail-method-filter-turn-completed\"]").Change(false);

        Assert.DoesNotContain("Sent turn/start", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Received turn/completed", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Received item/agentMessage/delta", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("turn_completed", cut.Markup, StringComparison.Ordinal);
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
                    null,
                    2,
                    true,
                    "Finished sessions keep the last known session ID and attempt when available.",
                    new DashboardSessionTokenUsageSnapshot(
                        120,
                        45,
                        165,
                        118,
                        40,
                        158,
                        130,
                        50,
                        47,
                        9,
                        177,
                        SessionTokenComparisonStatus.Mismatch,
                        12,
                        7,
                        19,
                        DateTimeOffset.UtcNow.AddMinutes(-1),
                        DateTimeOffset.UtcNow,
                        new DashboardSessionTokenOperationSnapshot(
                            "turn-4:thread_tokenUsage_updated:177",
                            "thread_tokenUsage_updated",
                            DateTimeOffset.UtcNow,
                            4,
                            130,
                            50,
                            47,
                            9,
                            177)))));

        Assert.Contains("177", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("130", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("50", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("47", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("9", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Rep In", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Rep Out", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Rep Total", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Reported Input Tokens", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Reported Output Tokens", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Cached Input Tokens", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Reasoning Tokens", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Reported Total Tokens", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Estimated Input Tokens", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Estimated Output Tokens", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Estimated Total Tokens", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Token Comparison", cut.Markup, StringComparison.Ordinal);
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

        Services.AddDashboardPageDataServices(dashboardStateService, runtimeService, store, CreateFollowUpActionResolutionService());

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

        Services.AddDashboardPageDataServices(
            dashboardStateService,
            new StaticRuntimeService(issueSnapshot: null),
            store,
            CreateFollowUpActionResolutionService());

        using var cut = Render<SessionDetailPage>(
            parameters => parameters.Add(component => component.Identifier, "ABC-2"));

        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.Equal(1, dashboardStateService.CallCount);
        Assert.Contains("data-testid=\"session-detail-metadata\"", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessionDetailPage_resolves_fake_follow_up_action_and_updates_fake_state()
    {
        Services.AddDashboardPageDataServices(
            new CountingDashboardStateService(CreateLiveSnapshot()),
            configureOptions: options =>
            {
                options.EnableFakeDataMode = true;
                options.DebugMode = true;
                options.TrackAgentMessageDeltas = true;
            });
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo("http://localhost/sessions/ABC-303?mode=fake");

        using var cut = Render<SessionDetailPage>(
            parameters => parameters.Add(component => component.Identifier, "ABC-303"));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("data-testid=\"session-detail-follow-up-action-panel\"", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Deployment target requires a manual approval step.", cut.Markup, StringComparison.Ordinal);
        });

        await cut.InvokeAsync(() => cut.Find("button").Click());

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("data-testid=\"session-detail-follow-up-action-panel\"", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Session resumed", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("fake-thread-303-turn-4", cut.Markup, StringComparison.Ordinal);
        });
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

    private static FollowUpActionResolutionService CreateFollowUpActionResolutionService()
    {
        var workflowOptionsProvider = new StaticWorkflowOptionsProvider(
            new WorkflowServiceOptions(
                new WorkflowTrackerOptions(
                    "github",
                    "https://api.github.com",
                    "token",
                    null,
                    "owner/repo",
                    null,
                    null,
                    ["Todo"],
                    ["Done"]),
                new WorkflowPollingOptions(1_000),
                new WorkflowWorkspaceOptions(Path.Combine(Path.GetTempPath(), "symphony-tests")),
                new WorkflowHookOptions(
                    null,
                    null,
                    null,
                    null,
                    60_000),
                new WorkflowAgentOptions(
                    1,
                    20,
                    300_000,
                    new Dictionary<string, int>(StringComparer.Ordinal),
                    false,
                    "exec:agent"),
                new WorkflowCodexOptions(
                    "codex app-server",
                    null,
                    null,
                    null,
                    3_600_000,
                    5_000,
                    300_000)));
        var queue = new OrchestratorDispatchQueue(
            workflowOptionsProvider,
            new RetryDelayPlanner(() => 1d),
            TimeProvider.System,
            NullLogger<OrchestratorDispatchQueue>.Instance);

        return new FollowUpActionResolutionService(
            new FollowUpActionRegistry(TimeProvider.System),
            queue,
            new StubIssueTrackerClient(),
            workflowOptionsProvider);
    }

    private static DashboardSnapshot CreateLiveSnapshot()
    {
        var startedAt = new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero);

        return new DashboardSnapshot(
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
            RunningCount: 0,
            RetryingCount: 0,
            InputTokens: 0,
            OutputTokens: 0,
            TotalTokens: 0,
            SecondsRunning: 0d,
            ActiveSessions: [],
            RetryQueue: [],
            RecentAttempts: [],
            LastError: null,
            WorkflowLastError: null);
    }

    private sealed class StaticWorkflowOptionsProvider(WorkflowServiceOptions workflowOptions) : IWorkflowOptionsProvider
    {
        public Task<WorkflowServiceOptions> GetCurrentAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(workflowOptions);
        }
    }

    private sealed class StubIssueTrackerClient : IIssueTrackerClient
    {
        public Task<IReadOnlyList<Issue>> FetchCandidateIssuesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Issue>>(Array.Empty<Issue>());
        }

        public Task<IReadOnlyList<Issue>> FetchIssuesByStatesAsync(
            IReadOnlyCollection<string> stateNames,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Issue>>(Array.Empty<Issue>());
        }

        public Task<IReadOnlyList<Issue>> FetchIssueStatesByIdsAsync(
            IReadOnlyCollection<string> issueIds,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Issue>>(Array.Empty<Issue>());
        }
    }

    private static SessionActivityTimelineModel CreateStructuredTimeline()
    {
        return new SessionActivityTimelineModel(
            [
                new SessionActivityTimelineEntryModel(
                    SessionActivityKind.AgentMessage,
                    new DateTimeOffset(2026, 3, 20, 9, 2, 0, TimeSpan.Zero),
                    "2026-03-20 09:02:00 UTC",
                    "turn_completed",
                    "Agent message",
                    Color.Info,
                    "Applied changes",
                    [],
                    "Applied changes",
                    null,
                    null,
                    false,
                    false,
                    Color.Info),
                new SessionActivityTimelineEntryModel(
                    SessionActivityKind.DebugMessage,
                    new DateTimeOffset(2026, 3, 20, 9, 2, 1, TimeSpan.Zero),
                    "2026-03-20 09:02:01 UTC",
                    "Sent turn/start",
                    "Debug transcript",
                    Color.Info,
                    "Method: turn/start | Prompt: Prompt body",
                    [
                        new SessionActivityFactModel("Method", "turn/start"),
                        new SessionActivityFactModel("Prompt", "Prompt body")
                    ],
                    "{\n  \"id\": 3,\n  \"method\": \"turn/start\",\n  \"params\": {\n    \"threadId\": \"thread-123\",\n    \"input\": [\n      {\n        \"type\": \"text\",\n        \"text\": \"Prompt body\"\n      }\n    ]\n  }\n}",
                    "Method: turn/start | Prompt: Prompt body",
                    "View structured payload",
                    true,
                    true,
                    Color.Info),
                new SessionActivityTimelineEntryModel(
                    SessionActivityKind.DebugMessage,
                    new DateTimeOffset(2026, 3, 20, 9, 2, 2, TimeSpan.Zero),
                    "2026-03-20 09:02:02 UTC",
                    "Received turn/completed",
                    "Debug transcript",
                    Color.Info,
                    "Method: turn/completed | Message: done",
                    [
                        new SessionActivityFactModel("Method", "turn/completed"),
                        new SessionActivityFactModel("Message", "done")
                    ],
                    "{\n  \"method\": \"turn/completed\",\n  \"params\": {\n    \"message\": \"done\"\n  }\n}",
                    "Method: turn/completed | Message: done",
                    "View structured payload",
                    true,
                    true,
                    Color.Info),
                new SessionActivityTimelineEntryModel(
                    SessionActivityKind.DebugMessage,
                    new DateTimeOffset(2026, 3, 20, 9, 2, 3, TimeSpan.Zero),
                    "2026-03-20 09:02:03 UTC",
                    "Received item/agentMessage/delta",
                    "Debug transcript",
                    Color.Info,
                    "Method: item/agentMessage/delta | Message: partial reply",
                    [
                        new SessionActivityFactModel("Method", "item/agentMessage/delta"),
                        new SessionActivityFactModel("Message", "partial reply")
                    ],
                    "{\n  \"method\": \"item/agentMessage/delta\",\n  \"params\": {\n    \"delta\": \"partial reply\"\n  }\n}",
                    "Method: item/agentMessage/delta | Message: partial reply",
                    "View structured payload",
                    true,
                    true,
                    Color.Info)
            ],
            LatestAttentionAlert: null,
            FailureAlert: null);
    }
}
