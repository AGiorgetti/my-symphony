using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Symphony.Abstractions.Orchestration;
using Symphony.Abstractions.Trackers;
using Symphony.Application.Configuration;
using Symphony.Application.Orchestration;
using Symphony.Application.Polling;
using Symphony.Application.Runtime;
using Symphony.Host.Api;
using Symphony.Host.Configuration;
using Symphony.Host.Dashboard;
using Symphony.Host.Health;
using Symphony.Domain.Issues;
using Symphony.Domain.Sessions;

namespace Symphony.Host.IntegrationTests;

public sealed class SymphonyApiEndpointRouteBuilderExtensionsTests
{
    [Fact]
    public async Task State_endpoint_returns_runtime_snapshot()
    {
        var pollingStatusTracker = new PollingStatusTracker();
        pollingStatusTracker.RecordStarted(new DateTimeOffset(2026, 3, 16, 14, 59, 55, TimeSpan.Zero));
        pollingStatusTracker.RecordCompleted(new DateTimeOffset(2026, 3, 16, 14, 59, 55, TimeSpan.Zero));
        using var app = await StartApiApplicationAsync(
            new StubRuntimeService
            {
                StateSnapshot = new OrchestratorStateSnapshot(
                    new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero),
                    [
                        new RunningIssueSnapshot(
                            "1",
                            "ABC-1",
                            "In Progress",
                            "thread-1-turn-1",
                            3,
                            "turn_completed",
                            "Done",
                            new DateTimeOffset(2026, 3, 16, 14, 55, 0, TimeSpan.Zero),
                            new DateTimeOffset(2026, 3, 16, 14, 59, 0, TimeSpan.Zero),
                            100,
                            40,
                            140)
                    ],
                    [
                        new Symphony.Abstractions.Orchestration.RetryDispatchSnapshot(
                            "2",
                            "ABC-2",
                            2,
                            new DateTimeOffset(2026, 3, 16, 15, 1, 0, TimeSpan.Zero),
                            "retry later")
                    ],
                    new CodexTotalsSnapshot(100, 40, 140, 300d),
                    RateLimits: null)
            },
            pollingStatusTracker,
            new StaticWorkflowLoadStatusReader(
                new WorkflowLoadStatusSnapshot(
                    "Loaded",
                    "C:\\repo\\WORKFLOW.md",
                    new DateTimeOffset(2026, 3, 16, 14, 58, 0, TimeSpan.Zero),
                    null,
                    null,
                    null,
                    1_000)),
            new FakeTimeProvider(new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero)));
        var client = CreateHttpClient(app);

        var response = await client.GetAsync("/api/v1/state");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<StateResponseDto>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Counts.Running);
        Assert.Equal(1, payload.Counts.Retrying);
        Assert.Equal("Healthy", payload.Health.Status);
        Assert.Equal(OrchestratorControlState.Started, payload.Health.OrchestratorState);
        Assert.Equal("Loaded", payload.Health.WorkflowLoadStatus);
        Assert.Equal(5d, payload.Health.LastSuccessfulPollAgeSeconds);
        Assert.Equal(OrchestratorControlState.Started, payload.Orchestration.State);
        Assert.Equal("ABC-1", payload.Running[0].IssueIdentifier);
        Assert.Equal("ABC-2", payload.Retrying[0].IssueIdentifier);
    }

    [Fact]
    public async Task Issue_endpoint_returns_not_found_error_envelope_for_unknown_issue()
    {
        using var app = await StartApiApplicationAsync(new StubRuntimeService());
        var client = CreateHttpClient(app);

        var response = await client.GetAsync("/api/v1/ABC-404");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ErrorEnvelopeDto>();
        Assert.NotNull(payload);
        Assert.Equal("issue_not_found", payload!.Error.Code);
    }

    [Fact]
    public async Task State_endpoint_includes_blocked_counts_and_issue_summaries()
    {
        using var app = await StartApiApplicationAsync(
            new StubRuntimeService
            {
                StateSnapshot = new OrchestratorStateSnapshot(
                    new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero),
                    Array.Empty<RunningIssueSnapshot>(),
                    Array.Empty<Symphony.Abstractions.Orchestration.RetryDispatchSnapshot>(),
                    new CodexTotalsSnapshot(0, 0, 0, 0d),
                    RateLimits: null,
                    Blocked:
                    [
                        new BlockedDispatchSnapshot(
                            "1",
                            "ABC-1",
                            "orch-123",
                            2,
                            new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero),
                            BlockingReasonCode.InputRequired,
                            "Need human input",
                            "Provide the required input or choose an option, then resolve the follow-up action to resume the run.",
                            "fai-1")
                    ],
                    FollowUpActions:
                    [
                        new FollowUpActionSnapshot(
                            "fai-1",
                            "1",
                            "ABC-1",
                            "orch-123",
                            new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero),
                            BlockingReasonCode.InputRequired,
                            "Need human input",
                            "Provide the required input or choose an option, then resolve the follow-up action to resume the run.",
                            [new FollowUpActionOptionSnapshot("resume", "Resume", "Continue after review.")],
                            FollowUpActionStatus.Pending,
                            ResolvedBy: null,
                            ResolvedAt: null,
                            SelectedOptionId: null,
                            Notes: null)
                    ])
            });
        var client = CreateHttpClient(app);

        var response = await client.GetAsync("/api/v1/state");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<StateResponseDto>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Counts.Blocked);
        var blocked = Assert.Single(payload.Blocked);
        Assert.Equal("ABC-1", blocked.IssueIdentifier);
        Assert.Equal("orch-123", blocked.OrchestratorSessionId);
        Assert.Equal("fai-1", blocked.FollowUpActionId);
    }

    [Fact]
    public async Task Issue_endpoint_returns_blocked_issue_and_follow_up_actions()
    {
        using var app = await StartApiApplicationAsync(
            new StubRuntimeService
            {
                IssueSnapshot = new OrchestratorIssueSnapshot(
                    "ABC-1",
                    "1",
                    "blocked_error",
                    RestartCount: 1,
                    CurrentRetryAttempt: 2,
                    Running: null,
                    Retry: null,
                    LastError: "Need human input",
                    RecentEvents: [],
                    OrchestratorSessionId: "orch-123",
                    Blocked: new BlockedDispatchSnapshot(
                        "1",
                        "ABC-1",
                        "orch-123",
                        2,
                        new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero),
                        BlockingReasonCode.InputRequired,
                        "Need human input",
                        "Provide the required input or choose an option, then resolve the follow-up action to resume the run.",
                        "fai-1"),
                    FollowUpActions:
                    [
                        new FollowUpActionSnapshot(
                            "fai-1",
                            "1",
                            "ABC-1",
                            "orch-123",
                            new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero),
                            BlockingReasonCode.InputRequired,
                            "Need human input",
                            "Provide the required input or choose an option, then resolve the follow-up action to resume the run.",
                            [new FollowUpActionOptionSnapshot("resume", "Resume", "Continue after review.")],
                            FollowUpActionStatus.Pending,
                            ResolvedBy: null,
                            ResolvedAt: null,
                            SelectedOptionId: null,
                            Notes: null)
                    ])
            });
        var client = CreateHttpClient(app);

        var response = await client.GetAsync("/api/v1/ABC-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<IssueResponseDto>();
        Assert.NotNull(payload);
        Assert.Equal("orch-123", payload!.OrchestratorSessionId);
        Assert.NotNull(payload.Blocked);
        Assert.Equal("fai-1", payload.Blocked!.FollowUpActionId);
        var action = Assert.Single(payload.FollowUpActions);
        Assert.Equal("fai-1", action.FollowUpActionId);
        Assert.Equal("Pending", action.Status);
    }

    [Fact]
    public async Task Refresh_endpoint_returns_accepted_receipt()
    {
        using var app = await StartApiApplicationAsync(
            new StubRuntimeService
            {
                RefreshReceipt = new PollingRefreshReceipt(
                    Queued: true,
                    Coalesced: false,
                    RequestedAt: new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero),
                    Operations: ["poll", "reconcile"])
            });
        var client = CreateHttpClient(app);

        var response = await client.PostAsJsonAsync("/api/v1/refresh", new { });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RefreshResponseDto>();
        Assert.NotNull(payload);
        Assert.True(payload!.Queued);
        Assert.False(payload.Coalesced);
        Assert.Equal(["poll", "reconcile"], payload.Operations);
    }

    [Fact]
    public async Task Export_session_endpoint_returns_downloadable_json_for_tracked_session()
    {
        var sessionStore = new SessionActivityStore(Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionActivityStore>.Instance);
        var startedAt = new DateTimeOffset(2026, 4, 2, 8, 0, 0, TimeSpan.Zero);
        sessionStore.RecordSessionStart("ABC-1", startedAt, "https://example.invalid/issues/ABC-1");
        sessionStore.RecordSessionMetadata(
            "ABC-1",
            startedAt.AddMinutes(2),
            new LiveSessionMetadata(
                "thread-1",
                "turn-1",
                lastCodexEvent: "turn_completed",
                lastCodexTimestamp: startedAt.AddMinutes(2),
                lastCodexMessage: "Applied change",
                codexInputTokens: 10,
                codexOutputTokens: 5,
                codexTotalTokens: 15,
                estimatedInputTokens: 8,
                estimatedOutputTokens: 4,
                estimatedTotalTokens: 12,
                lastReportedInputTokens: 10,
                lastReportedCachedInputTokens: 3,
                lastReportedOutputTokens: 5,
                lastReportedReasoningTokens: 2,
                lastReportedTotalTokens: 15,
                tokenComparisonStatus: SessionTokenComparisonStatus.Mismatch,
                tokenInputDelta: 2,
                tokenOutputDelta: 1,
                tokenTotalDelta: 3,
                lastEstimatedTokenAt: startedAt.AddMinutes(1),
                lastReportedTokenAt: startedAt.AddMinutes(2),
                lastUsageOperation: new SessionTokenUsageOperation(
                    "thread-1-turn-1:turn_completed",
                    "turn_completed",
                    startedAt.AddMinutes(2),
                    1,
                    10,
                    3,
                    5,
                    2,
                    15),
                turnCount: 1),
            attempt: 1,
            orchestratorSessionId: "orch-1");
        sessionStore.RecordActivity("ABC-1", new SessionActivityEntry(SessionActivityKind.AgentMessage, startedAt.AddMinutes(1), "turn_completed", "Applied change"));
        sessionStore.RecordActivity("ABC-1", new SessionActivityEntry(SessionActivityKind.DebugMessage, startedAt.AddMinutes(2), "Received item/agentMessage/delta", "{\"method\":\"item/agentMessage/delta\",\"params\":{\"turnId\":\"turn-1\"}}"));

        using var app = await StartApiApplicationAsync(
            new StubRuntimeService
            {
                IssueSnapshot = new OrchestratorIssueSnapshot(
                    "ABC-1",
                    "1",
                    "running",
                    RestartCount: 0,
                    CurrentRetryAttempt: 1,
                    Running: new RunningIssueSnapshot(
                        "1",
                        "ABC-1",
                        "StreamingTurn",
                        "session-1",
                        1,
                        "turn_completed",
                        "Applied change",
                        startedAt,
                        startedAt.AddMinutes(1),
                        10,
                        5,
                        15,
                        "orch-1"),
                    Retry: null,
                    LastError: null,
                    RecentEvents: [],
                    OrchestratorSessionId: "orch-1")
            },
            sessionActivityStore: sessionStore,
            dashboardStateService: new StaticDashboardStateService(CreateDashboardSnapshotForApi()));
        var client = CreateHttpClient(app);

        var response = await client.GetAsync("/api/v1/export/sessions/ABC-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var payload = JsonSerializer.Deserialize<DashboardDataExportEnvelope>(
            await response.Content.ReadAsStringAsync(),
            CreateExportJsonOptions());
        Assert.NotNull(payload);
        Assert.Equal(DashboardDataExportSchema.SingleSessionKind, payload!.ExportKind);
        Assert.Equal("ABC-1", payload.SingleSession!.Session.IssueIdentifier);
        Assert.NotNull(payload.SingleSession.History);
        Assert.Contains(payload.SingleSession.History!.Activities, activity => activity.Kind == SessionActivityKind.DebugMessage);
        Assert.NotNull(payload.SingleSession.Metadata);
        Assert.Equal(10, payload.SingleSession.Metadata!.InputTokens);
        Assert.Equal(5, payload.SingleSession.Metadata.OutputTokens);
        Assert.Equal(15, payload.SingleSession.Metadata.TotalTokens);
        Assert.NotNull(payload.SingleSession.Metadata.TokenUsage);
        Assert.Equal(12, payload.SingleSession.Metadata.TokenUsage!.EstimatedTotalTokens);
        Assert.Equal(3, payload.SingleSession.Metadata.TokenUsage.ReportedCachedInputTokens);
        Assert.Equal(2, payload.SingleSession.Metadata.TokenUsage.ReportedReasoningTokens);
        Assert.Equal(15, payload.SingleSession.Metadata.TokenUsage.ReportedTotalTokens);
        Assert.NotNull(payload.SingleSession.Metadata.TokenUsage.LastOperation);
        Assert.Equal("thread-1-turn-1:turn_completed", payload.SingleSession.Metadata.TokenUsage.LastOperation!.OperationId);
        Assert.NotNull(payload.SingleSession.History!.Metadata);
        Assert.Equal(15, payload.SingleSession.History.Metadata!.TokenUsage!.EffectiveTotalTokens);
    }

    [Fact]
    public async Task Export_orchestration_endpoint_returns_full_bundle_json()
    {
        var sessionStore = new SessionActivityStore(Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionActivityStore>.Instance);
        var startedAt = new DateTimeOffset(2026, 4, 2, 8, 0, 0, TimeSpan.Zero);
        sessionStore.RecordSessionStart("ABC-1", startedAt, "https://example.invalid/issues/ABC-1");
        sessionStore.RecordActivity("ABC-1", new SessionActivityEntry(SessionActivityKind.DebugMessage, startedAt.AddMinutes(1), "Sent turn/start", "{\"method\":\"turn/start\",\"params\":{\"threadId\":\"thread-1\",\"turnId\":\"turn-1\"}}"));

        using var app = await StartApiApplicationAsync(
            new StubRuntimeService
            {
                StateSnapshot = new OrchestratorStateSnapshot(
                    DateTimeOffset.UtcNow,
                    [],
                    [],
                    new CodexTotalsSnapshot(0, 0, 0, 0d),
                    RateLimits: null)
            },
            sessionActivityStore: sessionStore,
            dashboardStateService: new StaticDashboardStateService(CreateDashboardSnapshotForApi()));
        var client = CreateHttpClient(app);

        var response = await client.GetAsync("/api/v1/export/orchestration");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = JsonSerializer.Deserialize<DashboardDataExportEnvelope>(
            await response.Content.ReadAsStringAsync(),
            CreateExportJsonOptions());
        Assert.NotNull(payload);
        Assert.Equal(DashboardDataExportSchema.FullBundleKind, payload!.ExportKind);
        Assert.NotNull(payload.Bundle);
        Assert.Single(payload.Bundle!.Sessions);
        Assert.Contains(payload.Bundle.Sessions[0].Activities, activity => activity.Kind == SessionActivityKind.DebugMessage);
        Assert.NotNull(payload.Bundle.Sessions[0].Metadata);
    }

    [Fact]
    public async Task Orchestration_endpoint_returns_control_snapshot()
    {
        var control = new StubOrchestratorControl
        {
            Snapshot = new OrchestratorControlSnapshot(
                OrchestratorControlState.Stopped,
                new DateTimeOffset(2026, 3, 16, 14, 57, 0, TimeSpan.Zero))
        };
        using var app = await StartApiApplicationAsync(new StubRuntimeService(), orchestratorControl: control);
        var client = CreateHttpClient(app);

        var response = await client.GetAsync("/api/v1/orchestration");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<OrchestrationStatusDto>();
        Assert.NotNull(payload);
        Assert.Equal(OrchestratorControlState.Stopped, payload!.State);
    }

    [Fact]
    public async Task Start_endpoint_resumes_orchestrator_and_returns_updated_snapshot()
    {
        var control = new StubOrchestratorControl
        {
            Snapshot = new OrchestratorControlSnapshot(
                OrchestratorControlState.Stopped,
                new DateTimeOffset(2026, 3, 16, 14, 57, 0, TimeSpan.Zero))
        };
        using var app = await StartApiApplicationAsync(new StubRuntimeService(), orchestratorControl: control);
        var client = CreateHttpClient(app);

        var response = await client.PostAsJsonAsync("/api/v1/orchestration/start", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, control.ResumeCalls);
        var payload = await response.Content.ReadFromJsonAsync<OrchestrationStatusDto>();
        Assert.NotNull(payload);
        Assert.Equal(OrchestratorControlState.Started, payload!.State);
    }

    [Fact]
    public async Task Stop_endpoint_pauses_orchestrator_and_returns_updated_snapshot()
    {
        var control = new StubOrchestratorControl();
        using var app = await StartApiApplicationAsync(new StubRuntimeService(), orchestratorControl: control);
        var client = CreateHttpClient(app);

        var response = await client.PostAsJsonAsync("/api/v1/orchestration/stop", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, control.PauseCalls);
        var payload = await response.Content.ReadFromJsonAsync<OrchestrationStatusDto>();
        Assert.NotNull(payload);
        Assert.Equal(OrchestratorControlState.Stopped, payload!.State);
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

    private static async Task<WebApplication> StartApiApplicationAsync(
        IOrchestratorRuntimeService runtimeService,
        PollingStatusTracker? pollingStatusTracker = null,
        IWorkflowLoadStatusReader? workflowLoadStatusReader = null,
        TimeProvider? timeProvider = null,
        StubOrchestratorControl? orchestratorControl = null,
        IDashboardStateService? dashboardStateService = null,
        ISessionActivityStore? sessionActivityStore = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IOrchestratorRuntimeService>(runtimeService);
        var resolvedControl = orchestratorControl ?? new StubOrchestratorControl();
        builder.Services.AddSingleton<IOrchestratorControl>(resolvedControl);
        builder.Services.AddSingleton<IOrchestratorControlStatusReader>(resolvedControl);
        builder.Services.AddSingleton(pollingStatusTracker ?? new PollingStatusTracker());
        builder.Services.AddSingleton<IWorkflowLoadStatusReader>(
            workflowLoadStatusReader
            ?? new StaticWorkflowLoadStatusReader(
                new WorkflowLoadStatusSnapshot("Starting", null, null, null, null, null, null)));
        var resolvedTimeProvider = timeProvider ?? TimeProvider.System;
        var workflowOptionsProvider = CreateWorkflowOptionsProvider();
        builder.Services.AddSingleton(resolvedTimeProvider);
        builder.Services.AddSingleton<ServiceHealthSnapshotProvider>();
        builder.Services.AddSingleton<IWorkflowOptionsProvider>(workflowOptionsProvider);
        builder.Services.AddSingleton(CreateFollowUpActionResolutionService(workflowOptionsProvider, resolvedTimeProvider));
        builder.Services.AddSingleton<IHostEnvironment>(new StubHostEnvironment());
        builder.Services.AddSingleton<ISessionActivityStore>(sessionActivityStore ?? new SessionActivityStore(Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionActivityStore>.Instance));
        builder.Services.AddSingleton<IDashboardStateService>(dashboardStateService ?? new StaticDashboardStateService(CreateDashboardSnapshotForApi()));
        builder.Services.Configure<DashboardUiOptions>(_ => { });
        builder.Services.AddSingleton<IDashboardDataExportService, DashboardDataExportService>();

        var app = builder.Build();
        app.MapSymphonyApi();
        await app.StartAsync();
        return app;
    }

    private sealed class StubRuntimeService : IOrchestratorRuntimeService
    {
        public OrchestratorStateSnapshot StateSnapshot { get; set; } = new(
            DateTimeOffset.UtcNow,
            Array.Empty<RunningIssueSnapshot>(),
            Array.Empty<Symphony.Abstractions.Orchestration.RetryDispatchSnapshot>(),
            new CodexTotalsSnapshot(0, 0, 0, 0d),
            RateLimits: null);

        public OrchestratorIssueSnapshot? IssueSnapshot { get; set; }

        public PollingRefreshReceipt RefreshReceipt { get; set; } = new(
            Queued: true,
            Coalesced: false,
            RequestedAt: DateTimeOffset.UtcNow,
            Operations: ["poll", "reconcile"]);

        public Task<OrchestratorStateSnapshot> GetStateSnapshotAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(StateSnapshot);
        }

        public Task<OrchestratorIssueSnapshot?> GetIssueSnapshotAsync(
            string issueIdentifier,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(IssueSnapshot);
        }

        public PollingRefreshReceipt RequestRefresh()
        {
            return RefreshReceipt;
        }
    }

    private sealed class StaticWorkflowOptionsProvider(WorkflowServiceOptions workflowOptions) : IWorkflowOptionsProvider
    {
        public Task<WorkflowServiceOptions> GetCurrentAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(workflowOptions);
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

    private static FollowUpActionResolutionService CreateFollowUpActionResolutionService(
        IWorkflowOptionsProvider workflowOptionsProvider,
        TimeProvider timeProvider)
    {
        var queue = new OrchestratorDispatchQueue(
            workflowOptionsProvider,
            new RetryDelayPlanner(() => 1d),
            timeProvider,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OrchestratorDispatchQueue>.Instance);

        return new FollowUpActionResolutionService(
            new FollowUpActionRegistry(timeProvider),
            queue,
            new StubIssueTrackerClient(),
            workflowOptionsProvider);
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

    private sealed class StaticWorkflowLoadStatusReader(WorkflowLoadStatusSnapshot snapshot) : IWorkflowLoadStatusReader
    {
        public WorkflowLoadStatusSnapshot GetSnapshot()
        {
            return snapshot;
        }
    }

    private sealed class StaticDashboardStateService(DashboardSnapshot snapshot) : IDashboardStateService
    {
        public Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(snapshot);
        }
    }

    private static DashboardSnapshot CreateDashboardSnapshotForApi()
    {
        return new DashboardSnapshot(
            DateTimeOffset.UtcNow,
            "Healthy",
            "Single-process in-memory",
            OrchestratorControlState.Started,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            0d,
            "Loaded",
            DateTimeOffset.UtcNow,
            0,
            0,
            0,
            0,
            0,
            0d,
            [],
            [],
            [],
            null,
            null);
    }

    private static JsonSerializerOptions CreateExportJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";

        public string ApplicationName { get; set; } = "Symphony.Host.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(AppContext.BaseDirectory);
    }

    private sealed class StubOrchestratorControl : IOrchestratorControl, IOrchestratorControlStatusReader
    {
        public OrchestratorControlSnapshot Snapshot { get; set; } = new(
            OrchestratorControlState.Started,
            DateTimeOffset.UtcNow);

        public int PauseCalls { get; private set; }

        public int ResumeCalls { get; private set; }

        public Task RequestRefreshAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken cancellationToken = default)
        {
            PauseCalls++;
            Snapshot = Snapshot with
            {
                State = OrchestratorControlState.Stopped,
                ChangedAt = DateTimeOffset.UtcNow
            };
            return Task.CompletedTask;
        }

        public Task ResumeAsync(CancellationToken cancellationToken = default)
        {
            ResumeCalls++;
            Snapshot = Snapshot with
            {
                State = OrchestratorControlState.Started,
                ChangedAt = DateTimeOffset.UtcNow
            };
            return Task.CompletedTask;
        }

        public OrchestratorControlSnapshot GetSnapshot()
        {
            return Snapshot;
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
