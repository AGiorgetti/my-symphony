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

namespace Symphony.Host.IntegrationTests;

public sealed class SymphonyApiEndpointRouteBuilderExtensionsTests
{
    [Fact]
    public async Task State_endpoint_returns_runtime_snapshot()
    {
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
            });
        var client = CreateHttpClient(app);

        var response = await client.GetAsync("/api/v1/state");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<StateResponseDto>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Counts.Running);
        Assert.Equal(1, payload.Counts.Retrying);
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

    private static async Task<WebApplication> StartApiApplicationAsync(IOrchestratorRuntimeService runtimeService)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IOrchestratorRuntimeService>(runtimeService);
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
}
