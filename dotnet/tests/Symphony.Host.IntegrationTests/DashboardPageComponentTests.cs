using Bunit;
using Flowbite.Services;
using Microsoft.Extensions.DependencyInjection;
using Symphony.Abstractions.Orchestration;
using Symphony.Host.Components.Dashboard;
using Symphony.Host.Components.Pages;
using Symphony.Host.Dashboard;

namespace Symphony.Host.IntegrationTests;

public sealed class DashboardPageComponentTests : BunitContext
{
    public DashboardPageComponentTests()
    {
        Services.AddFlowbite();
    }

    [Fact]
    public void DashboardPage_shows_skeleton_while_initial_snapshot_is_loading()
    {
        var taskSource = new TaskCompletionSource<DashboardSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        Services.AddSingleton<IDashboardStateService>(new DeferredDashboardStateService(taskSource.Task));

        var cut = Render<DashboardPage>();

        Assert.Contains("data-testid=\"dashboard-skeleton\"", cut.Markup, StringComparison.Ordinal);

        taskSource.SetResult(CreateDashboardSnapshot());

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("data-testid=\"dashboard-skeleton\"", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("data-testid=\"dashboard-summary-grid\"", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void HealthSummaryCards_renders_failure_and_warning_alerts_from_snapshot()
    {
        var cut = Render<HealthSummaryCards>(parameters => parameters
            .Add(component => component.Snapshot, CreateDashboardSnapshot(
                serviceHealth: "Degraded",
                lastError: "Tracker request failed",
                workflowLastError: "Workflow syntax is invalid.")));

        Assert.Contains("data-testid=\"dashboard-summary-alerts\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Polling failure:", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Workflow reload warning:", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void HealthSummaryCards_renders_exec_marker_dispatch_policy_alert_when_enabled()
    {
        var cut = Render<HealthSummaryCards>(parameters => parameters
            .Add(component => component.Snapshot, CreateDashboardSnapshot(requireExecMarker: true)));

        Assert.Contains("Dispatch policy:", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Only issues labeled or tagged `exec:agent` are eligible for agent scheduling.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ActiveSessionsPanel_renders_empty_state_when_no_sessions_are_running()
    {
        var cut = Render<ActiveSessionsPanel>(parameters => parameters
            .Add(component => component.Sessions, Array.Empty<DashboardActiveSessionSnapshot>())
            .Add(component => component.GeneratedAt, new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero)));

        Assert.Contains("No active sessions", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("New in-flight work will appear here", cut.Markup, StringComparison.Ordinal);
    }

    private sealed class DeferredDashboardStateService(Task<DashboardSnapshot> snapshotTask) : IDashboardStateService
    {
        public Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            return snapshotTask;
        }
    }

    private static DashboardSnapshot CreateDashboardSnapshot(
        string serviceHealth = "Healthy",
        string? lastError = null,
        string? workflowLastError = null,
        bool requireExecMarker = false)
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
            ActiveSessions: [],
            RetryQueue: [],
            RecentAttempts: [],
            lastError,
            workflowLastError,
            requireExecMarker);
    }
}
