using System.Net;
using System.Net.Http.Json;
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
                    "Healthy",
                    "Single-process in-memory",
                    new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero),
                    RunningCount: 2,
                    RetryingCount: 1,
                    InputTokens: 100,
                    OutputTokens: 40,
                    TotalTokens: 140,
                    SecondsRunning: 300d,
                    LastError: null)));
        var client = CreateHttpClient(app);

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Runtime Control Surface", html, StringComparison.Ordinal);
        Assert.Contains("Service Health", html, StringComparison.Ordinal);
        Assert.Contains("Single-process in-memory", html, StringComparison.Ordinal);
        Assert.Contains("2026-03-16 15:00:00 UTC", html, StringComparison.Ordinal);
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
        builder.Services.AddSingleton<IOrchestratorRuntimeService>(runtimeService);
        builder.Services.AddSingleton<IDashboardStateService>(dashboardStateService);
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
