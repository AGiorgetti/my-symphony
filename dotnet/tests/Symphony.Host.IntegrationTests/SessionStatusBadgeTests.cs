using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Symphony.Domain.Runs;
using Symphony.Host.Components.Sessions;

namespace Symphony.Host.IntegrationTests;

public sealed class SessionStatusBadgeTests : BunitContext
{
    public SessionStatusBadgeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IKeyInterceptorService, TestKeyInterceptorService>();
    }

    [Theory]
    [InlineData(RunAttemptStatus.Succeeded, "status-pill--success")]
    [InlineData(RunAttemptStatus.PreparingWorkspace, "status-pill--info")]
    [InlineData(RunAttemptStatus.BuildingPrompt, "status-pill--info")]
    [InlineData(RunAttemptStatus.LaunchingAgentProcess, "status-pill--info")]
    [InlineData(RunAttemptStatus.InitializingSession, "status-pill--info")]
    [InlineData(RunAttemptStatus.StreamingTurn, "status-pill--info")]
    [InlineData(RunAttemptStatus.Finishing, "status-pill--info")]
    [InlineData(RunAttemptStatus.Failed, "status-pill--error")]
    [InlineData(RunAttemptStatus.TimedOut, "status-pill--error")]
    [InlineData(RunAttemptStatus.Stalled, "status-pill--error")]
    [InlineData(RunAttemptStatus.CanceledByReconciliation, "status-pill--default")]
    public void SessionStatusBadge_maps_status_to_expected_badge_color(
        RunAttemptStatus status,
        string expectedClass)
    {
        var cut = Render<SessionStatusBadge>(parameters => parameters.Add(component => component.Status, status));

        Assert.Contains(status.ToString(), cut.Markup, StringComparison.Ordinal);
        Assert.Contains(expectedClass, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionStatusBadge_uses_custom_text_and_color_override_when_status_is_not_available()
    {
        var cut = Render<SessionStatusBadge>(
            parameters => parameters
                .Add(component => component.Text, "Active")
                .Add(component => component.ColorOverride, Color.Info));

        Assert.Contains("Active", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("status-pill--info", cut.Markup, StringComparison.Ordinal);
    }
}
