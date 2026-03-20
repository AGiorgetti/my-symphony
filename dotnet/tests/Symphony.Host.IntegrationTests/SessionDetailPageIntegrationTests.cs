using System.Net;
using System.Net.Http.Json;
using Flowbite.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Symphony.Application.Configuration;
using Symphony.Application.Polling;
using Symphony.Application.Runtime;
using Symphony.Host.Api;
using Symphony.Host.Components;
using Symphony.Host.Dashboard;
using Symphony.Host.Theming;

namespace Symphony.Host.IntegrationTests;

public sealed class SessionDetailPageIntegrationTests
{
    [Fact]
    public async Task Session_detail_page_renders_breadcrumb_header_and_timeline()
    {
        var store = CreateStoreWithActiveSession();
        using var app = await StartSessionDetailApplicationAsync(store, new StaticDashboardStateService(CreateSnapshot()));
        var client = CreateHttpClient(app);

        var response = await client.GetAsync("/sessions/ABC-1");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-testid=\"session-detail-breadcrumb\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/sessions\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-detail-header\"", html, StringComparison.Ordinal);
        Assert.Contains("Open tracker issue", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-header-active-indicator\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-detail-timeline\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-detail-latest-attention-alert\"", html, StringComparison.Ordinal);

        var startedIndex = html.IndexOf("Session started", StringComparison.Ordinal);
        var messageIndex = html.IndexOf("turn_completed", StringComparison.Ordinal);
        var warningIndex = html.LastIndexOf("Queued for retry", StringComparison.Ordinal);

        Assert.True(startedIndex >= 0);
        Assert.True(messageIndex > startedIndex);
        Assert.True(warningIndex > messageIndex);
    }

    [Fact]
    public async Task Session_detail_page_renders_failure_alert_for_final_error()
    {
        var store = CreateStoreWithFailedSession();
        using var app = await StartSessionDetailApplicationAsync(store, new StaticDashboardStateService(CreateSnapshot(activeSessions: [])));
        var client = CreateHttpClient(app);

        var response = await client.GetAsync("/sessions/ABC-2");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Failed", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-detail-failure-alert\"", html, StringComparison.Ordinal);
        Assert.Contains("Prompt build failed", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Session_detail_page_renders_not_found_message_for_unknown_session()
    {
        var store = new SessionActivityStore(NullLogger<SessionActivityStore>.Instance);
        using var app = await StartSessionDetailApplicationAsync(store, new StaticDashboardStateService(CreateSnapshot(activeSessions: [])));
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
        using var app = await StartSessionDetailApplicationAsync(store, new StaticDashboardStateService(CreateSnapshot()));
        var client = CreateHttpClient(app);

        var pageResponse = await client.GetAsync("/sessions/ABC-1");
        var apiResponse = await client.PostAsJsonAsync("/api/v1/refresh", new { });

        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, apiResponse.StatusCode);
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

    private static SessionActivityStore CreateStoreWithFailedSession()
    {
        var store = new SessionActivityStore(NullLogger<SessionActivityStore>.Instance);
        var startedAt = new DateTimeOffset(2026, 3, 20, 8, 0, 0, TimeSpan.Zero);
        var endedAt = startedAt.AddMinutes(5);

        store.RecordSessionStart("ABC-2", startedAt, "https://github.com/AGiorgetti/my-symphony/issues/ABC-2");
        store.RecordActivity("ABC-2", new SessionActivityEntry(SessionActivityKind.LifecycleMilestone, startedAt, "Session started", "Tracker moved to In Progress"));
        store.RecordActivity("ABC-2", new SessionActivityEntry(SessionActivityKind.Error, endedAt, "Prompt build failed", "The workflow prompt could not be assembled."));
        store.RecordSessionEnd("ABC-2", endedAt, "Failed", "Prompt build failed");

        return store;
    }

    private static DashboardSnapshot CreateSnapshot(IReadOnlyList<DashboardActiveSessionSnapshot>? activeSessions = null)
    {
        return new DashboardSnapshot(
            new DateTimeOffset(2026, 3, 20, 9, 5, 0, TimeSpan.Zero),
            "Healthy",
            "Single-process in-memory",
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
            RecentAttempts: [],
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
        IDashboardStateService dashboardStateService)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddFlowbite();
        builder.Services.AddSingleton<IOrchestratorRuntimeService>(new StubRuntimeService());
        builder.Services.AddSingleton(sessionActivityStore);
        builder.Services.AddSingleton<ISessionActivityStore>(sessionActivityStore);
        builder.Services.AddSingleton<IDashboardStateService>(dashboardStateService);
        builder.Services.AddScoped<IThemeService, ThemeService>();
        builder.Services.AddScoped<ThemeService>();
        builder.Services.AddSingleton<IWorkflowOptionsProvider>(
            new StaticWorkflowOptionsProvider(
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
                        new Dictionary<string, int>(StringComparer.Ordinal)),
                    new WorkflowCodexOptions(
                        "codex app-server",
                        null,
                        null,
                        null,
                        3_600_000,
                        5_000,
                        300_000))));

        var app = builder.Build();
        app.UseStaticFiles();
        app.UseAntiforgery();
        app.MapSymphonyApi();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        await app.StartAsync();
        return app;
    }

    private sealed class StaticDashboardStateService(DashboardSnapshot snapshot) : IDashboardStateService
    {
        public Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(snapshot);
        }
    }

    private sealed class StaticWorkflowOptionsProvider(WorkflowServiceOptions workflowOptions) : IWorkflowOptionsProvider
    {
        public Task<WorkflowServiceOptions> GetCurrentAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(workflowOptions);
        }
    }

    private sealed class StubRuntimeService : IOrchestratorRuntimeService
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
            return Task.FromResult<OrchestratorIssueSnapshot?>(null);
        }

        public PollingRefreshReceipt RequestRefresh()
        {
            return new PollingRefreshReceipt(true, false, DateTimeOffset.UtcNow, ["poll", "reconcile"]);
        }
    }
}
