using Bunit;
using Flowbite.Components;
using Flowbite.Services;
using Microsoft.Extensions.DependencyInjection;
using Symphony.Domain.Runs;
using Symphony.Host.Components.Sessions;

namespace Symphony.Host.IntegrationTests;

public sealed class SessionStatusBadgeTests : BunitContext
{
    public SessionStatusBadgeTests()
    {
        Services.AddFlowbite();
    }

    [Theory]
    [InlineData(RunAttemptStatus.Succeeded, Badge.BadgeColor.Success)]
    [InlineData(RunAttemptStatus.PreparingWorkspace, Badge.BadgeColor.Info)]
    [InlineData(RunAttemptStatus.BuildingPrompt, Badge.BadgeColor.Info)]
    [InlineData(RunAttemptStatus.LaunchingAgentProcess, Badge.BadgeColor.Info)]
    [InlineData(RunAttemptStatus.InitializingSession, Badge.BadgeColor.Info)]
    [InlineData(RunAttemptStatus.StreamingTurn, Badge.BadgeColor.Info)]
    [InlineData(RunAttemptStatus.Finishing, Badge.BadgeColor.Info)]
    [InlineData(RunAttemptStatus.Failed, Badge.BadgeColor.Failure)]
    [InlineData(RunAttemptStatus.TimedOut, Badge.BadgeColor.Failure)]
    [InlineData(RunAttemptStatus.Stalled, Badge.BadgeColor.Failure)]
    [InlineData(RunAttemptStatus.CanceledByReconciliation, Badge.BadgeColor.Gray)]
    public void SessionStatusBadge_maps_status_to_expected_badge_color(
        RunAttemptStatus status,
        Badge.BadgeColor expectedColor)
    {
        var cut = Render<SessionStatusBadge>(parameters => parameters.Add(component => component.Status, status));

        var badge = cut.FindComponent<Badge>();

        Assert.Equal(expectedColor, badge.Instance.Color);
        Assert.Contains(status.ToString(), cut.Markup, StringComparison.Ordinal);
    }
}
