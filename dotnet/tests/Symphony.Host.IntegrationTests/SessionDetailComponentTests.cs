using Bunit;
using Flowbite.Components;
using Flowbite.Services;
using Microsoft.Extensions.DependencyInjection;
using Symphony.Host.Components.SessionDetail;
using Symphony.Host.Dashboard;

namespace Symphony.Host.IntegrationTests;

public sealed class SessionDetailComponentTests : BunitContext
{
    public SessionDetailComponentTests()
    {
        Services.AddFlowbite();
    }

    [Fact]
    public void SessionHeaderCard_renders_status_tracker_link_and_spinner_for_active_sessions()
    {
        var cut = Render<SessionHeaderCard>(
            parameters => parameters.Add(
                component => component.Session,
                new SessionHeaderDisplayModel(
                    "ABC-1",
                    "https://github.com/AGiorgetti/my-symphony/issues/ABC-1",
                    true,
                    "Active",
                    Status: null,
                    Badge.BadgeColor.Info,
                    new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero),
                    EndedAt: null)));

        Assert.Contains("ABC-1", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Open tracker issue", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Session is active", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Still running", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionActivityTimeline_renders_warning_and_failure_alerts()
    {
        var cut = Render<SessionActivityTimeline>(
            parameters => parameters.Add(
                component => component.Timeline,
                new SessionActivityTimelineModel(
                    [
                        new SessionActivityTimelineEntryModel(
                            SessionActivityKind.LifecycleMilestone,
                            new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero),
                            "Session started",
                            "Tracker moved to In Progress",
                            TimelineColor.Gray),
                        new SessionActivityTimelineEntryModel(
                            SessionActivityKind.Warning,
                            new DateTimeOffset(2026, 3, 20, 9, 1, 0, TimeSpan.Zero),
                            "Queued for retry",
                            "Waiting for the next dispatcher slot",
                            TimelineColor.Orange)
                    ],
                    new SessionActivityTimelineAlertModel(AlertColor.Warning, "Latest warning:", "Queued for retry - Waiting for the next dispatcher slot"),
                    new SessionActivityTimelineAlertModel(AlertColor.Failure, "Failure detail:", "Prompt build failed"))));

        Assert.Contains("data-testid=\"session-detail-latest-attention-alert\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"session-detail-failure-alert\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Session started", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Queued for retry", cut.Markup, StringComparison.Ordinal);
    }
}
