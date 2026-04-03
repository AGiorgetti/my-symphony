using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using Symphony.Abstractions.Orchestration;
using Symphony.Abstractions.Trackers;
using Symphony.Application.Configuration;
using Symphony.Application.Orchestration;
using Symphony.Application.Polling;
using Symphony.Application.Runtime;
using Symphony.Host.Api;
using Symphony.Host.Components;
using Symphony.Host.Configuration;
using Symphony.Host.Dashboard;
using Symphony.Host.Theming;
using Symphony.Domain.Issues;
using Symphony.Domain.Sessions;

namespace Symphony.Host.IntegrationTests;

public sealed class SessionDetailPageIntegrationTests
{
    [Fact]
    public async Task Session_detail_page_renders_breadcrumb_activity_and_compact_metadata()
    {
        var store = CreateStoreWithActiveSession();
        using var app = await StartSessionDetailApplicationAsync(
            store,
            new StaticDashboardStateService(CreateSnapshot()),
            CreateActiveRuntimeService());
        var client = CreateHttpClient(app);

        var response = await client.GetAsync("/sessions/ABC-1");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-testid=\"session-detail-breadcrumb\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/sessions\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-detail-header\"", html, StringComparison.Ordinal);
        Assert.Contains("Open tracker issue", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-detail-export-link\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/api/v1/export/sessions/ABC-1\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-header-active-indicator\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-detail-metadata-panel\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-detail-metadata-summary\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-detail-metadata\"", html, StringComparison.Ordinal);
        Assert.Contains("thread-1-turn-2", html, StringComparison.Ordinal);
        Assert.Contains("Attempt 2", html, StringComparison.Ordinal);
        Assert.Contains("110", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-detail-latest-attention-alert\"", html, StringComparison.Ordinal);

        var startedIndex = html.IndexOf("Session started", StringComparison.Ordinal);
        var messageIndex = html.IndexOf("Turn Completed", StringComparison.Ordinal);
        var warningIndex = html.LastIndexOf("Queued for retry", StringComparison.Ordinal);

        Assert.True(startedIndex >= 0);
        Assert.True(messageIndex > startedIndex);
        Assert.True(warningIndex > messageIndex);
    }

    [Fact]
    public async Task Session_detail_page_renders_failure_alert_and_ended_metadata_note()
    {
        var store = CreateStoreWithFailedSession();
        using var app = await StartSessionDetailApplicationAsync(
            store,
            new StaticDashboardStateService(
                CreateSnapshot(
                    activeSessions: [],
                    recentAttempts:
                    [
                        new DashboardRecentAttemptSnapshot(
                            "ABC-2",
                            1,
                            "Failed",
                            new DateTimeOffset(2026, 3, 20, 8, 5, 0, TimeSpan.Zero),
                            300d,
                            "Prompt build failed",
                            "thread-2-turn-3")
                    ])),
            new StaticRuntimeService(issueSnapshot: null));
        var client = CreateHttpClient(app);

        var response = await client.GetAsync("/sessions/ABC-2");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Failed", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-detail-failure-alert\"", html, StringComparison.Ordinal);
        Assert.Contains("Prompt build failed", html, StringComparison.Ordinal);
        Assert.Contains("thread-2-turn-3", html, StringComparison.Ordinal);
        Assert.Contains("Attempt 1", html, StringComparison.Ordinal);
        Assert.Contains("Finished sessions keep the last known session ID and token totals when available.", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-detail-metadata-estimated-total-tokens\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-detail-metadata-reported-total-tokens\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-detail-metadata-cached-input-tokens\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-detail-metadata-reasoning-tokens\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-detail-metadata-comparison-status\"", html, StringComparison.Ordinal);
        Assert.Contains(">90<", html, StringComparison.Ordinal);
        Assert.Contains(">96<", html, StringComparison.Ordinal);
        Assert.Contains(">12<", html, StringComparison.Ordinal);
        Assert.Contains(">9<", html, StringComparison.Ordinal);
        Assert.Contains("Mismatch", html, StringComparison.Ordinal);
        Assert.Contains("6", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-detail-timeline-entry-token-indicator\"", html, StringComparison.Ordinal);
        Assert.Contains("Tokens: Available", html, StringComparison.Ordinal);
        Assert.Contains("Entry total", html, StringComparison.Ordinal);
        Assert.Contains("Reported total", html, StringComparison.Ordinal);
        Assert.Contains("Current total", html, StringComparison.Ordinal);
        Assert.Contains("Estimated total", html, StringComparison.Ordinal);
        Assert.Contains("Comparison", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Session_detail_page_hides_raw_payloads_when_debug_mode_is_disabled()
    {
        var store = CreateStoreWithStructuredPayloadSession();
        using var app = await StartSessionDetailApplicationAsync(
            store,
            new StaticDashboardStateService(CreateSnapshot()),
            CreateActiveRuntimeService(),
            debugModeEnabled: false);
        var client = CreateHttpClient(app);

        var response = await client.GetAsync("/sessions/ABC-3");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Turn completed", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"session-detail-debug-banner\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"session-detail-timeline-detail\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Sent turn/start", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Received turn/completed", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Prompt body", html, StringComparison.Ordinal);
        Assert.DoesNotContain("&quot;message&quot;: &quot;done&quot;", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Session_detail_page_renders_structured_payloads_when_debug_mode_is_enabled()
    {
        var store = CreateStoreWithStructuredPayloadSession();
        using var app = await StartSessionDetailApplicationAsync(
            store,
            new StaticDashboardStateService(CreateSnapshot()),
            CreateActiveRuntimeService(),
            debugModeEnabled: true,
            trackAgentMessageDeltasEnabled: true);
        var client = CreateHttpClient(app);

        var response = await client.GetAsync("/sessions/ABC-3");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-testid=\"session-detail-debug-banner\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-detail-method-filter\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-detail-method-filter-turn-start\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-detail-method-filter-turn-completed\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-detail-method-filter-item-agentmessage-delta\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-detail-timeline-detail\"", html, StringComparison.Ordinal);
        Assert.Contains("Sent turn/start", html, StringComparison.Ordinal);
        Assert.Contains("Received turn/completed", html, StringComparison.Ordinal);
        Assert.Contains("Received item/agentMessage/delta", html, StringComparison.Ordinal);
        Assert.Contains("View raw payload and debug metadata", html, StringComparison.Ordinal);
        Assert.Contains("Debug metadata", html, StringComparison.Ordinal);
        Assert.Contains("Raw payload", html, StringComparison.Ordinal);
        Assert.Contains("Prompt body", html, StringComparison.Ordinal);
        Assert.Contains("&quot;method&quot;: &quot;turn/completed&quot;", html, StringComparison.Ordinal);
        Assert.Contains("&quot;message&quot;: &quot;done&quot;", html, StringComparison.Ordinal);
        Assert.Contains("&quot;method&quot;: &quot;item/agentMessage/delta&quot;", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Session_detail_page_renders_not_found_message_for_unknown_session()
    {
        var store = new SessionActivityStore(NullLogger<SessionActivityStore>.Instance);
        using var app = await StartSessionDetailApplicationAsync(
            store,
            new StaticDashboardStateService(CreateSnapshot(activeSessions: [])),
            new StaticRuntimeService(issueSnapshot: null));
        var client = CreateHttpClient(app);

        var response = await client.GetAsync("/sessions/ABC-404");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-testid=\"session-detail-not-found\"", html, StringComparison.Ordinal);
        Assert.Contains("ABC-404", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Session_detail_page_keeps_api_routes_available_when_rendering_succeeds()
    {
        var store = CreateStoreWithActiveSession();
        using var app = await StartSessionDetailApplicationAsync(
            store,
            new StaticDashboardStateService(CreateSnapshot()),
            CreateActiveRuntimeService());
        var client = CreateHttpClient(app);

        var pageResponse = await client.GetAsync("/sessions/ABC-1");
        var apiResponse = await client.PostAsJsonAsync("/api/v1/refresh", new { });

        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, apiResponse.StatusCode);
    }

    [Fact]
    public async Task Session_detail_page_renders_fake_dataset_from_direct_fake_mode_url()
    {
        using var app = await StartSessionDetailApplicationAsync(
            new SessionActivityStore(NullLogger<SessionActivityStore>.Instance),
            new StaticDashboardStateService(CreateSnapshot()),
            new StaticRuntimeService(issueSnapshot: null),
            debugModeEnabled: true,
            trackAgentMessageDeltasEnabled: true,
            enableFakeDataMode: true);
        var client = CreateHttpClient(app);

        var response = await client.GetAsync("/sessions/ABC-404?mode=fake");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Prompt build failed", html, StringComparison.Ordinal);
        Assert.Contains("fake-thread-404-turn-3", html, StringComparison.Ordinal);
        Assert.Contains("Sent initialize", html, StringComparison.Ordinal);
        Assert.Contains("Received response 1", html, StringComparison.Ordinal);
        Assert.Contains("item/agentMessage/delta", html, StringComparison.Ordinal);
        Assert.Contains("Trace sample 36", html, StringComparison.Ordinal);
        Assert.Contains("Sent turn/start", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/?mode=fake\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/sessions?mode=fake\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Session_detail_page_renders_follow_up_action_panel_for_blocked_issue()
    {
        var store = new SessionActivityStore(NullLogger<SessionActivityStore>.Instance);
        var startedAt = new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero);
        store.RecordSessionStart("ABC-9", startedAt, "https://github.com/AGiorgetti/my-symphony/issues/ABC-9");
        store.RecordActivity(
            "ABC-9",
            new SessionActivityEntry(
                SessionActivityKind.AttentionRequired,
                startedAt.AddMinutes(2),
                "Follow-up action created",
                "Need a human choice Action: Review the requested manual decision, then resolve the follow-up action to resume the run."));
        using var app = await StartSessionDetailApplicationAsync(
            store,
            new StaticDashboardStateService(CreateSnapshot(activeSessions: [])),
            CreateBlockedRuntimeService());
        var client = CreateHttpClient(app);

        var response = await client.GetAsync("/sessions/ABC-9");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-testid=\"session-detail-follow-up-action-panel\"", html, StringComparison.Ordinal);
        Assert.Contains("Needs attention", html, StringComparison.Ordinal);
        Assert.Contains("Need a human choice", html, StringComparison.Ordinal);
        Assert.Contains("Review the requested manual decision, then resolve the follow-up action to resume the run.", html, StringComparison.Ordinal);
        Assert.Contains("orch-999", html, StringComparison.Ordinal);
    }

    private static SessionActivityStore CreateStoreWithActiveSession()
    {
        var store = new SessionActivityStore(NullLogger<SessionActivityStore>.Instance);
        var startedAt = new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero);

        store.RecordSessionStart("ABC-1", startedAt, "https://github.com/AGiorgetti/my-symphony/issues/ABC-1");
        store.RecordActivity("ABC-1", new SessionActivityEntry(SessionActivityKind.LifecycleMilestone, startedAt, "Session started", "Tracker moved to In Progress"));
        store.RecordActivity("ABC-1", new SessionActivityEntry(SessionActivityKind.AgentMessage, startedAt.AddMinutes(1), "turn_completed", "Applied changes"));
        store.RecordActivity("ABC-1", new SessionActivityEntry(SessionActivityKind.Warning, startedAt.AddMinutes(2), "Queued for retry", "Waiting for the next dispatcher slot"));

        return store;
    }

    private static SessionActivityStore CreateStoreWithStructuredPayloadSession()
    {
        var store = new SessionActivityStore(NullLogger<SessionActivityStore>.Instance);
        var startedAt = new DateTimeOffset(2026, 3, 20, 10, 0, 0, TimeSpan.Zero);

        store.RecordSessionStart("ABC-3", startedAt, "https://github.com/AGiorgetti/my-symphony/issues/ABC-3");
        store.RecordActivity("ABC-3", new SessionActivityEntry(SessionActivityKind.AgentMessage, startedAt.AddSeconds(30), "Turn completed", "Applied changes"));
        store.RecordActivity(
            "ABC-3",
            new SessionActivityEntry(
                SessionActivityKind.DebugMessage,
                startedAt.AddMinutes(1),
                "Sent turn/start",
                """
                {
                  "id": 3,
                  "method": "turn/start",
                  "params": {
                    "threadId": "thread-123",
                    "input": [
                      {
                        "type": "text",
                        "text": "Prompt body"
                      }
                    ]
                  }
                }
                """));
        store.RecordActivity(
            "ABC-3",
            new SessionActivityEntry(
                SessionActivityKind.DebugMessage,
                startedAt.AddMinutes(1).AddSeconds(1),
                "Received turn/completed",
                """
                {
                  "method": "turn/completed",
                  "params": {
                    "message": "done",
                    "usage": {
                      "input_tokens": 12,
                      "output_tokens": 5,
                      "total_tokens": 17
                    }
                  }
                }
                """));
        store.RecordActivity(
            "ABC-3",
            new SessionActivityEntry(
                SessionActivityKind.DebugMessage,
                startedAt.AddMinutes(1).AddSeconds(2),
                "Received item/agentMessage/delta",
                """
                {
                  "method": "item/agentMessage/delta",
                  "params": {
                    "delta": "partial reply"
                  }
                }
                """));

        return store;
    }

    private static SessionActivityStore CreateStoreWithFailedSession()
    {
        var store = new SessionActivityStore(NullLogger<SessionActivityStore>.Instance);
        var startedAt = new DateTimeOffset(2026, 3, 20, 8, 0, 0, TimeSpan.Zero);
        var endedAt = startedAt.AddMinutes(5);

        store.RecordSessionStart("ABC-2", startedAt, "https://github.com/AGiorgetti/my-symphony/issues/ABC-2");
        store.RecordSessionMetadata(
            "ABC-2",
            endedAt,
            new LiveSessionMetadata(
                "thread-2",
                "turn-3",
                lastCodexEvent: "turn_failed",
                lastCodexTimestamp: endedAt,
                lastCodexMessage: "The workflow prompt could not be assembled.",
                codexInputTokens: 64,
                codexOutputTokens: 32,
                codexTotalTokens: 96,
                estimatedInputTokens: 60,
                estimatedOutputTokens: 30,
                estimatedTotalTokens: 90,
                lastReportedInputTokens: 64,
                lastReportedCachedInputTokens: 12,
                lastReportedOutputTokens: 32,
                lastReportedReasoningTokens: 9,
                lastReportedTotalTokens: 96,
                tokenComparisonStatus: SessionTokenComparisonStatus.Mismatch,
                tokenInputDelta: 4,
                tokenOutputDelta: 2,
                tokenTotalDelta: 6,
                lastEstimatedTokenAt: endedAt.AddSeconds(-10),
                lastReportedTokenAt: endedAt,
                lastUsageOperation: new SessionTokenUsageOperation(
                    "thread-2-turn-3:turn_failed",
                    "turn_failed",
                    endedAt,
                    3,
                    64,
                    12,
                    32,
                    9,
                    96),
                turnCount: 3),
            attempt: 1,
            orchestratorSessionId: "orch-2");
        store.RecordActivity("ABC-2", new SessionActivityEntry(SessionActivityKind.LifecycleMilestone, startedAt, "Session started", "Tracker moved to In Progress"));
        store.RecordActivity("ABC-2", new SessionActivityEntry(SessionActivityKind.Error, endedAt, "Prompt build failed", "The workflow prompt could not be assembled."));
        store.RecordSessionEnd("ABC-2", endedAt, "Failed", "Prompt build failed");

        return store;
    }

    private static DashboardSnapshot CreateSnapshot(
        IReadOnlyList<DashboardActiveSessionSnapshot>? activeSessions = null,
        IReadOnlyList<DashboardRecentAttemptSnapshot>? recentAttempts = null)
    {
        return new DashboardSnapshot(
            new DateTimeOffset(2026, 3, 20, 9, 5, 0, TimeSpan.Zero),
            "Healthy",
            "Single-process in-memory",
            OrchestratorControlState.Started,
            new DateTimeOffset(2026, 3, 20, 9, 5, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 20, 9, 5, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 20, 9, 4, 40, TimeSpan.Zero),
            20d,
            "Loaded",
            new DateTimeOffset(2026, 3, 20, 8, 55, 0, TimeSpan.Zero),
            RunningCount: activeSessions?.Count ?? 1,
            RetryingCount: 0,
            InputTokens: 80,
            OutputTokens: 30,
            TotalTokens: 110,
            SecondsRunning: 420d,
            activeSessions ??
            [
                new DashboardActiveSessionSnapshot(
                    "ABC-1",
                    "In Progress",
                    "thread-1-turn-2",
                    2,
                    "turn_completed",
                    "Applied changes",
                    new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 3, 20, 9, 2, 0, TimeSpan.Zero),
                    110)
            ],
            RetryQueue: [],
            RecentAttempts: recentAttempts ?? [],
            LastError: null,
            WorkflowLastError: null);
    }

    private static HttpClient CreateHttpClient(WebApplication app)
    {
        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Test server addresses are unavailable.");
        var address = Assert.Single(addresses.Addresses);

        return new HttpClient
        {
            BaseAddress = new Uri(address)
        };
    }

    private static async Task<WebApplication> StartSessionDetailApplicationAsync(
        ISessionActivityStore sessionActivityStore,
        IDashboardStateService dashboardStateService,
        IOrchestratorRuntimeService runtimeService,
        bool debugModeEnabled = false,
        bool trackAgentMessageDeltasEnabled = false,
        bool enableFakeDataMode = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddMudServices();
        builder.Services.Configure<DashboardUiOptions>(
            options =>
            {
                options.DebugMode = debugModeEnabled;
                options.TrackAgentMessageDeltas = trackAgentMessageDeltasEnabled;
            });
        builder.Services.AddSingleton<IOrchestratorControl>(new StubOrchestratorControl());
        builder.Services.AddDashboardPageDataServices(
            dashboardStateService,
            runtimeService,
            sessionActivityStore,
            CreateFollowUpActionResolutionService(),
            options =>
            {
                options.DebugMode = debugModeEnabled;
                options.TrackAgentMessageDeltas = trackAgentMessageDeltasEnabled;
                options.EnableFakeDataMode = enableFakeDataMode;
            });
        builder.Services.AddScoped<IThemeService, ThemeService>();
        builder.Services.AddScoped<ThemeService>();
        builder.Services.AddSingleton<IWorkflowOptionsProvider>(
            CreateWorkflowOptionsProvider());

        var app = builder.Build();
        app.UseStaticFiles();
        app.UseAntiforgery();
        app.MapSymphonyApi();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        await app.StartAsync();
        return app;
    }

    private static StaticRuntimeService CreateActiveRuntimeService()
    {
        return new StaticRuntimeService(
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
                    "thread-1-turn-2",
                    2,
                    "turn_completed",
                    "Applied changes",
                    new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 3, 20, 9, 2, 0, TimeSpan.Zero),
                    80,
                    30,
                    110),
                Retry: null,
                LastError: null,
                RecentEvents: []));
    }

    private static StaticRuntimeService CreateBlockedRuntimeService()
    {
        var blockedAt = new DateTimeOffset(2026, 3, 20, 9, 2, 0, TimeSpan.Zero);

        return new StaticRuntimeService(
            new OrchestratorIssueSnapshot(
                "ABC-9",
                "9",
                "blocked_error",
                RestartCount: 1,
                CurrentRetryAttempt: 2,
                Running: null,
                Retry: null,
                LastError: "Need a human choice",
                RecentEvents: [],
                OrchestratorSessionId: "orch-999",
                Blocked: new BlockedDispatchSnapshot(
                    "9",
                    "ABC-9",
                    "orch-999",
                    2,
                    blockedAt,
                    BlockingReasonCode.ManualDecisionRequired,
                    "Need a human choice",
                    "Review the requested manual decision, then resolve the follow-up action to resume the run.",
                    "fai-9"),
                FollowUpActions:
                [
                    new FollowUpActionSnapshot(
                        "fai-9",
                        "9",
                        "ABC-9",
                        "orch-999",
                        blockedAt,
                        BlockingReasonCode.ManualDecisionRequired,
                        "Need a human choice",
                        "Review the requested manual decision, then resolve the follow-up action to resume the run.",
                        [new FollowUpActionOptionSnapshot("resume", "Resume", "Continue after review.")],
                        FollowUpActionStatus.Pending,
                        ResolvedBy: null,
                        ResolvedAt: null,
                        SelectedOptionId: null,
                        Notes: null)
                ]));
    }

    private sealed class StaticDashboardStateService(DashboardSnapshot snapshot) : IDashboardStateService
    {
        public Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(snapshot);
        }
    }

    private static StaticWorkflowOptionsProvider CreateWorkflowOptionsProvider()
    {
        return new StaticWorkflowOptionsProvider(
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
    }

    private static FollowUpActionResolutionService CreateFollowUpActionResolutionService()
    {
        var workflowOptionsProvider = CreateWorkflowOptionsProvider();
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

    private sealed class StubOrchestratorControl : IOrchestratorControl
    {
        public Task RequestRefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ResumeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
