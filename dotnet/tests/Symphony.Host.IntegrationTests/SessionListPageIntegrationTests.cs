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

public sealed class SessionListPageIntegrationTests
{
    [Fact]
    public async Task Sessions_page_renders_active_and_ended_session_rows()
    {
        var store = CreateStore(
            new DashboardSnapshot(
                new DateTimeOffset(2026, 3, 20, 8, 0, 0, TimeSpan.Zero),
                "Healthy",
                "Single-process in-memory",
                new DateTimeOffset(2026, 3, 20, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 3, 20, 7, 59, 40, TimeSpan.Zero),
                20d,
                "Loaded",
                new DateTimeOffset(2026, 3, 20, 7, 55, 0, TimeSpan.Zero),
                RunningCount: 1,
                RetryingCount: 0,
                InputTokens: 80,
                OutputTokens: 30,
                TotalTokens: 110,
                SecondsRunning: 420d,
                ActiveSessions:
                [
                    new DashboardActiveSessionSnapshot(
                        "ABC-1",
                        "In Progress",
                        "thread-1-turn-2",
                        2,
                        "turn_completed",
                        "Applied changes",
                        new DateTimeOffset(2026, 3, 20, 7, 50, 0, TimeSpan.Zero),
                        new DateTimeOffset(2026, 3, 20, 7, 59, 30, TimeSpan.Zero),
                        110)
                ],
                RetryQueue: [],
                RecentAttempts: [],
                LastError: null,
                WorkflowLastError: null));
        using var app = await StartSessionListApplicationAsync(store, new StaticDashboardStateService(CreateSnapshot()));
        var client = CreateHttpClient(app);

        var response = await client.GetAsync("/sessions");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Session Explorer", html, StringComparison.Ordinal);
        Assert.Contains("All (2)", html, StringComparison.Ordinal);
        Assert.Contains("Active (1)", html, StringComparison.Ordinal);
        Assert.Contains("Ended (1)", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-list-table\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/sessions/ABC-1\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/sessions/ABC-2\"", html, StringComparison.Ordinal);
        Assert.Contains("In Progress", html, StringComparison.Ordinal);
        Assert.Contains("Succeeded", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sessions_page_renders_empty_state_when_store_is_empty()
    {
        var store = CreateStore(CreateSnapshot(activeSessions: []), includeEndedSession: false);
        using var app = await StartSessionListApplicationAsync(store, new StaticDashboardStateService(CreateSnapshot(activeSessions: [])));
        var client = CreateHttpClient(app);

        var response = await client.GetAsync("/sessions");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-testid=\"session-list-empty-state\"", html, StringComparison.Ordinal);
        Assert.Contains("No sessions in this view", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sessions_page_keeps_api_routes_available_when_rendering_succeeds()
    {
        var store = CreateStore(CreateSnapshot());
        using var app = await StartSessionListApplicationAsync(store, new StaticDashboardStateService(CreateSnapshot()));
        var client = CreateHttpClient(app);

        var pageResponse = await client.GetAsync("/sessions");
        var apiResponse = await client.PostAsJsonAsync("/api/v1/refresh", new { });

        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, apiResponse.StatusCode);
    }

    private static SessionActivityStore CreateStore(DashboardSnapshot snapshot, bool includeEndedSession = true)
    {
        var store = new SessionActivityStore(NullLogger<SessionActivityStore>.Instance);

        foreach (var activeSession in snapshot.ActiveSessions)
        {
            store.RecordSessionStart(activeSession.IssueIdentifier, activeSession.StartedAt, $"https://github.com/AGiorgetti/my-symphony/issues/{activeSession.IssueIdentifier}");
        }

        if (includeEndedSession)
        {
            store.RecordSessionStart("ABC-2", new DateTimeOffset(2026, 3, 20, 7, 15, 0, TimeSpan.Zero), "https://github.com/AGiorgetti/my-symphony/issues/ABC-2");
            store.RecordSessionEnd("ABC-2", new DateTimeOffset(2026, 3, 20, 7, 35, 0, TimeSpan.Zero), "Succeeded");
        }

        return store;
    }

    private static DashboardSnapshot CreateSnapshot(IReadOnlyList<DashboardActiveSessionSnapshot>? activeSessions = null)
    {
        return new DashboardSnapshot(
            new DateTimeOffset(2026, 3, 20, 8, 0, 0, TimeSpan.Zero),
            "Healthy",
            "Single-process in-memory",
            new DateTimeOffset(2026, 3, 20, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 20, 7, 59, 40, TimeSpan.Zero),
            20d,
            "Loaded",
            new DateTimeOffset(2026, 3, 20, 7, 55, 0, TimeSpan.Zero),
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
                    new DateTimeOffset(2026, 3, 20, 7, 50, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 3, 20, 7, 59, 30, TimeSpan.Zero),
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

    private static async Task<WebApplication> StartSessionListApplicationAsync(
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
