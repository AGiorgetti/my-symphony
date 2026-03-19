using System.Net;
using System.Net.Http.Json;
using Flowbite.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
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
    [Fact]
    public async Task Root_page_renders_dashboard_shell()
    {
        using var app = await StartDashboardApplicationAsync(
            new StubRuntimeService(),
            new StaticDashboardStateService(
                new DashboardSnapshot(
                    new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero),
                    "Healthy",
                    "Single-process in-memory",
                    new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 3, 16, 14, 59, 30, TimeSpan.Zero),
                    30d,
                    "Loaded",
                    new DateTimeOffset(2026, 3, 16, 14, 58, 0, TimeSpan.Zero),
                    RunningCount: 2,
                    RetryingCount: 1,
                    InputTokens: 100,
                    OutputTokens: 40,
                    TotalTokens: 140,
                    SecondsRunning: 300d,
                    ActiveSessions:
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
                    RetryQueue:
                    [
                        new DashboardRetrySnapshot(
                            "ABC-2",
                            2,
                            new DateTimeOffset(2026, 3, 16, 15, 1, 0, TimeSpan.Zero),
                            "retry later")
                    ],
                    RecentAttempts:
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
                    LastError: null,
                    WorkflowLastError: null)));
        var client = CreateHttpClient(app);

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("class=\"dark\"", html, StringComparison.Ordinal);
        Assert.Contains("data-theme=\"dark-yellow\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"app.min.css\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"_content/Flowbite/flowbite.min.css\"", html, StringComparison.Ordinal);
        Assert.Contains("https://cdn.jsdelivr.net/npm/@floating-ui/core@1.6.13/dist/floating-ui.core.umd.min.js", html, StringComparison.Ordinal);
        Assert.Contains("https://cdn.jsdelivr.net/npm/@floating-ui/dom@1.6.13/dist/floating-ui.dom.umd.min.js", html, StringComparison.Ordinal);
        Assert.Contains("src=\"_content/Flowbite/flowbite.js\"", html, StringComparison.Ordinal);
        Assert.Contains("Runtime Control Surface", html, StringComparison.Ordinal);
        Assert.Contains("Service Health", html, StringComparison.Ordinal);
        Assert.Contains("Workflow Config", html, StringComparison.Ordinal);
        Assert.Contains("Operator Dashboard", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/sessions\"", html, StringComparison.Ordinal);
        Assert.Contains("Live sessions", html, StringComparison.Ordinal);
        Assert.Contains(">2</span>", html, StringComparison.Ordinal);
        Assert.Contains("Single-process in-memory", html, StringComparison.Ordinal);
        Assert.Contains("2026-03-16 15:00:00 UTC", html, StringComparison.Ordinal);
        Assert.Contains("Loaded", html, StringComparison.Ordinal);
        Assert.Contains("Active Sessions", html, StringComparison.Ordinal);
        Assert.Contains("Retry Queue", html, StringComparison.Ordinal);
        Assert.Contains("Recent Attempts", html, StringComparison.Ordinal);
        Assert.Contains("ABC-1", html, StringComparison.Ordinal);
        Assert.Contains("ABC-2", html, StringComparison.Ordinal);
        Assert.Contains("ABC-3", html, StringComparison.Ordinal);
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
        IDashboardStateService dashboardStateService)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddFlowbite();
        builder.Services.AddSingleton<IOrchestratorRuntimeService>(runtimeService);
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
}
