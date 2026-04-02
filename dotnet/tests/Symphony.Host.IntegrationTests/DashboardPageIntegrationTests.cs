using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using MudBlazor.Services;
using Symphony.Abstractions.Orchestration;
using Symphony.Application.Configuration;
using Symphony.Application.Polling;
using Symphony.Application.Runtime;
using Symphony.Host.Api;
using Symphony.Host.Components;
using Symphony.Host.Dashboard;
using Symphony.Host.Theming;

namespace Symphony.Host.IntegrationTests;

public sealed class DashboardPageIntegrationTests
{
    private static readonly string HostProjectDirectory = ResolveHostProjectDirectory();

    [Fact]
    public async Task Root_page_renders_dashboard_shell()
    {
        using var app = await StartDashboardApplicationAsync(
            new StubRuntimeService(),
            new StaticDashboardStateService(CreateDashboardSnapshot()));
        var client = CreateHttpClient(app);

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("class=\"dark\"", html, StringComparison.Ordinal);
        Assert.Contains("data-theme=\"dark-yellow\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"favicon.svg\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"app.css\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"_content/MudBlazor/MudBlazor.min.css\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"_content/MudBlazor/MudBlazor.min.js\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"dashboard-summary-grid\"", html, StringComparison.Ordinal);
        Assert.Contains("Service Health", html, StringComparison.Ordinal);
        Assert.Contains("Workflow Config", html, StringComparison.Ordinal);
        Assert.Contains("Operator Dashboard", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"theme-switcher\"", html, StringComparison.Ordinal);
        Assert.Contains("Dark Yellow", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/sessions\"", html, StringComparison.Ordinal);
        Assert.Contains("Live sessions", html, StringComparison.Ordinal);
        Assert.Contains(">2</span>", html, StringComparison.Ordinal);
        Assert.Contains("Single-process in-memory", html, StringComparison.Ordinal);
        Assert.Contains("2026-03-16 15:00:00 UTC", html, StringComparison.Ordinal);
        Assert.Contains("Loaded", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"dashboard-active-sessions\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"dashboard-retry-queue\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"dashboard-recent-attempts\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"dashboard-export-all-link\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/api/v1/export/orchestration\"", html, StringComparison.Ordinal);
        Assert.Contains("ABC-1", html, StringComparison.Ordinal);
        Assert.Contains("ABC-2", html, StringComparison.Ordinal);
        Assert.Contains("ABC-3", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Root_page_static_stylesheet_is_served()
    {
        using var app = await StartDashboardApplicationAsync(
            new StubRuntimeService(),
            new StaticDashboardStateService(CreateDashboardSnapshot()));
        var client = CreateHttpClient(app);

        var response = await client.GetAsync("/app.css");
        var css = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/css", response.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);
        Assert.Contains(".dashboard-page", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Root_page_renders_empty_state_panels_and_summary_alerts()
    {
        using var app = await StartDashboardApplicationAsync(
            new StubRuntimeService(),
            new StaticDashboardStateService(
                CreateDashboardSnapshot(
                    serviceHealth: "Degraded",
                    lastError: "Tracker request failed",
                    workflowLastError: "Workflow syntax is invalid.",
                    activeSessions: [],
                    retryQueue: [],
                    recentAttempts: [])));
        var client = CreateHttpClient(app);

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-testid=\"dashboard-summary-alerts\"", html, StringComparison.Ordinal);
        Assert.Contains("Polling failure:", html, StringComparison.Ordinal);
        Assert.Contains("Tracker request failed", html, StringComparison.Ordinal);
        Assert.Contains("Workflow reload warning:", html, StringComparison.Ordinal);
        Assert.Contains("Workflow syntax is invalid.", html, StringComparison.Ordinal);
        Assert.Contains("No active sessions", html, StringComparison.Ordinal);
        Assert.Contains("Retry queue is empty", html, StringComparison.Ordinal);
        Assert.Contains("No recent attempts yet", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Root_page_renders_fake_dashboard_when_fake_mode_is_enabled()
    {
        using var app = await StartDashboardApplicationAsync(
            new StubRuntimeService(),
            new StaticDashboardStateService(CreateDashboardSnapshot()),
            enableFakeDataMode: true);
        var client = CreateHttpClient(app);

        var response = await client.GetAsync("/?mode=fake");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ABC-101", html, StringComparison.Ordinal);
        Assert.Contains("ABC-202", html, StringComparison.Ordinal);
        Assert.Contains("ABC-303", html, StringComparison.Ordinal);
        Assert.Contains("ABC-404", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/sessions/ABC-101?mode=fake\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"dashboard-fake-data-upload\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Root_page_loads_fake_data_from_configured_json_file()
    {
        var importPath = Path.Combine(Path.GetTempPath(), $"symphony-fake-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(
            importPath,
            JsonSerializer.Serialize(CreateImportedSingleSessionEnvelope(), new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        try
        {
            using var app = await StartDashboardApplicationAsync(
                new StubRuntimeService(),
                new StaticDashboardStateService(CreateDashboardSnapshot()),
                enableFakeDataMode: true,
                fakeDataJsonPath: importPath);
            var client = CreateHttpClient(app);

            var response = await client.GetAsync("/?mode=fake");
            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("ABC-101", html, StringComparison.Ordinal);
            Assert.Contains("IMP-1", html, StringComparison.Ordinal);
            Assert.Contains("Merged imported session &#x27;IMP-1&#x27;", html, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(importPath);
        }
    }

    [Fact]
    public async Task Root_page_ignores_fake_mode_query_when_fake_mode_is_disabled()
    {
        using var app = await StartDashboardApplicationAsync(
            new StubRuntimeService(),
            new StaticDashboardStateService(CreateDashboardSnapshot()));
        var client = CreateHttpClient(app);

        var response = await client.GetAsync("/?mode=fake");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ABC-1", html, StringComparison.Ordinal);
        Assert.DoesNotContain("ABC-101", html, StringComparison.Ordinal);
        Assert.DoesNotContain("?mode=fake", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Root_page_uses_error_boundary_without_blocking_api_routes()
    {
        using var app = await StartDashboardApplicationAsync(
            new StubRuntimeService(),
            new ThrowingDashboardStateService());
        var client = CreateHttpClient(app);

        var rootResponse = await client.GetAsync("/");
        var rootHtml = await rootResponse.Content.ReadAsStringAsync();
        var apiResponse = await client.PostAsJsonAsync("/api/v1/refresh", new { });

        Assert.Equal(HttpStatusCode.OK, rootResponse.StatusCode);
        Assert.Contains("Dashboard temporarily unavailable", rootHtml, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Accepted, apiResponse.StatusCode);
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

    private static async Task<WebApplication> StartDashboardApplicationAsync(
        IOrchestratorRuntimeService runtimeService,
        IDashboardStateService dashboardStateService,
        bool enableFakeDataMode = false,
        string? fakeDataJsonPath = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddMudServices();
        builder.Services.AddSingleton<IOrchestratorControl>(new StubOrchestratorControl());
        builder.Services.AddDashboardPageDataServices(
            dashboardStateService,
            runtimeService,
            configureOptions: options =>
            {
                options.EnableFakeDataMode = enableFakeDataMode;
                options.FakeDataJsonPath = fakeDataJsonPath;
            });
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
                        300_000))));

        var app = builder.Build();
        app.UseStaticFiles(
            new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(Path.Combine(HostProjectDirectory, "wwwroot"))
            });
        app.UseAntiforgery();
        app.MapSymphonyApi();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        await app.StartAsync();
        return app;
    }

    private static string ResolveHostProjectDirectory()
    {
        var hostProjectDirectory = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "src",
                "Symphony.Host"));

        return Directory.Exists(hostProjectDirectory)
            ? hostProjectDirectory
            : throw new DirectoryNotFoundException(
                $"Unable to locate the Symphony.Host project directory from '{AppContext.BaseDirectory}'.");
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

    private sealed class StaticDashboardStateService(DashboardSnapshot snapshot) : IDashboardStateService
    {
        public Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(snapshot);
        }
    }

    private sealed class ThrowingDashboardStateService : IDashboardStateService
    {
        public Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("dashboard render failure");
        }
    }

    private sealed class StaticWorkflowOptionsProvider(WorkflowServiceOptions workflowOptions) : IWorkflowOptionsProvider
    {
        public Task<WorkflowServiceOptions> GetCurrentAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(workflowOptions);
        }
    }

    private static DashboardSnapshot CreateDashboardSnapshot(
        string serviceHealth = "Healthy",
        string? lastError = null,
        string? workflowLastError = null,
        IReadOnlyList<DashboardActiveSessionSnapshot>? activeSessions = null,
        IReadOnlyList<DashboardRetrySnapshot>? retryQueue = null,
        IReadOnlyList<DashboardRecentAttemptSnapshot>? recentAttempts = null)
    {
        return new DashboardSnapshot(
            new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero),
            serviceHealth,
            "Single-process in-memory",
            OrchestratorControlState.Started,
            new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 16, 14, 59, 30, TimeSpan.Zero),
            30d,
            workflowLastError is null ? "Loaded" : "ReloadFailedUsingLastKnownGood",
            new DateTimeOffset(2026, 3, 16, 14, 58, 0, TimeSpan.Zero),
            RunningCount: 2,
            RetryingCount: 1,
            InputTokens: 100,
            OutputTokens: 40,
            TotalTokens: 140,
            SecondsRunning: 300d,
            activeSessions ??
            [
                new DashboardActiveSessionSnapshot(
                    "ABC-1",
                    "In Progress",
                    "thread-1-turn-1",
                    3,
                    "turn_completed",
                    "Applied changes",
                    new DateTimeOffset(2026, 3, 16, 14, 55, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 3, 16, 14, 59, 0, TimeSpan.Zero),
                    140)
            ],
            retryQueue ??
            [
                new DashboardRetrySnapshot(
                    "ABC-2",
                    2,
                    new DateTimeOffset(2026, 3, 16, 15, 1, 0, TimeSpan.Zero),
                    "retry later")
            ],
            recentAttempts ??
            [
                new DashboardRecentAttemptSnapshot(
                    "ABC-3",
                    1,
                    "Retrying",
                    new DateTimeOffset(2026, 3, 16, 14, 58, 30, TimeSpan.Zero),
                    22.5d,
                    "Tracker request failed",
                    "thread-3-turn-2")
            ],
            lastError,
            workflowLastError);
    }

    private static DashboardDataExportEnvelope CreateImportedSingleSessionEnvelope()
    {
        var session = new SessionRecord(
            "IMP-1",
            "https://example.invalid/issues/IMP-1",
            new DateTimeOffset(2026, 4, 2, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 2, 8, 4, 0, TimeSpan.Zero),
            "Succeeded",
            null,
            false);
        return new DashboardDataExportEnvelope(
            DashboardDataExportSchema.CurrentVersion,
            new DateTimeOffset(2026, 4, 2, 9, 0, 0, TimeSpan.Zero),
            DashboardDataExportSchema.SingleSessionKind,
            new DashboardDataExportSource("Symphony.Host.Tests", "1.0.0", "Development"),
            new DashboardDataSessionExport(
                session,
                [new SessionActivityEntry(SessionActivityKind.Outcome, session.EndedAt!.Value, "Run succeeded", "Imported from JSON.")],
                new DashboardSessionHistorySnapshot(
                    session,
                    [new SessionActivityEntry(SessionActivityKind.Outcome, session.EndedAt!.Value, "Run succeeded", "Imported from JSON.")]),
                IssueSnapshot: null,
                ActiveSession: null,
                RetryEntry: null,
                RecentAttempt: new DashboardRecentAttemptSnapshot("IMP-1", 1, "Succeeded", session.EndedAt.Value, 240d, null, "imp-session-1", "orch-imp-1"),
                BlockedSession: null,
                FollowUpActions: []),
            Bundle: null);
    }

    private sealed class StubOrchestratorControl : IOrchestratorControl
    {
        public Task RequestRefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ResumeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
