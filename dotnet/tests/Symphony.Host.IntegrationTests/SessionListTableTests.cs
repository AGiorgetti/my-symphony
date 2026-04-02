using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using MudBlazor;
using Symphony.Abstractions.Orchestration;
using Symphony.Host.Components.Pages;
using Symphony.Host.Components.Sessions;
using Symphony.Host.Dashboard;

namespace Symphony.Host.IntegrationTests;

public sealed class SessionListTableTests : BunitContext
{
    public SessionListTableTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IKeyInterceptorService, TestKeyInterceptorService>();
    }

    [Fact]
    public void SessionListTable_renders_empty_state_for_empty_results()
    {
        var cut = Render<SessionListTable>(parameters => parameters
            .Add(component => component.Sessions, Array.Empty<SessionListRowViewModel>())
            .Add(component => component.Mode, new DashboardPageMode(DashboardDataMode.Live)));

        Assert.Contains("data-testid=\"session-list-empty-state\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("No sessions in this view", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionListPage_switches_between_all_active_and_ended_tabs()
    {
        var store = new SessionActivityStore(Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionActivityStore>.Instance);
        store.RecordSessionStart("ABC-1", new DateTimeOffset(2026, 3, 20, 7, 50, 0, TimeSpan.Zero));
        store.RecordSessionStart("ABC-2", new DateTimeOffset(2026, 3, 20, 7, 10, 0, TimeSpan.Zero));
        store.RecordSessionEnd("ABC-2", new DateTimeOffset(2026, 3, 20, 7, 30, 0, TimeSpan.Zero), "Succeeded");

        Services.AddDashboardPageDataServices(
            new StaticDashboardStateService(
                new DashboardSnapshot(
                    new DateTimeOffset(2026, 3, 20, 8, 0, 0, TimeSpan.Zero),
                    "Healthy",
                    "Single-process in-memory",
                    OrchestratorControlState.Started,
                    new DateTimeOffset(2026, 3, 20, 8, 0, 0, TimeSpan.Zero),
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
                    WorkflowLastError: null)),
            sessionActivityStore: store);

        var cut = Render<SessionListPage>();

        Assert.Contains("ABC-1", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("ABC-2", cut.Markup, StringComparison.Ordinal);

        cut.FindAll(".mud-tab").Single(button => button.TextContent.Contains("Active", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("ABC-1", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("href=\"/sessions/ABC-2\"", cut.Markup, StringComparison.Ordinal);
        });

        cut.FindAll(".mud-tab").Single(button => button.TextContent.Contains("Ended", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("ABC-2", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("href=\"/sessions/ABC-1\"", cut.Markup, StringComparison.Ordinal);
        });
    }

    private sealed class StaticDashboardStateService(DashboardSnapshot snapshot) : IDashboardStateService
    {
        public Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(snapshot);
        }
    }
}
