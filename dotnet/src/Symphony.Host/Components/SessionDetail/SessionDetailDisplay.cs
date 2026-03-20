using System.Globalization;
using Flowbite.Components;
using Symphony.Domain.Runs;
using Symphony.Host.Dashboard;

namespace Symphony.Host.Components.SessionDetail;

internal static class SessionDetailDisplay
{
    internal static string FormatTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
    }

    internal static Badge.BadgeColor GetFallbackStatusColor(string statusText, bool isActive)
    {
        if (isActive)
        {
            return Badge.BadgeColor.Info;
        }

        return statusText.Trim().ToLowerInvariant() switch
        {
            "succeeded" => Badge.BadgeColor.Success,
            "retrying" => Badge.BadgeColor.Warning,
            "failed" => Badge.BadgeColor.Failure,
            "timedout" => Badge.BadgeColor.Failure,
            "stalled" => Badge.BadgeColor.Failure,
            "canceled" => Badge.BadgeColor.Gray,
            "canceledbyreconciliation" => Badge.BadgeColor.Gray,
            _ => Badge.BadgeColor.Gray
        };
    }

    internal static RunAttemptStatus? TryParseStatus(string statusText)
    {
        return statusText.Trim().ToLowerInvariant() switch
        {
            "preparingworkspace" => RunAttemptStatus.PreparingWorkspace,
            "buildingprompt" => RunAttemptStatus.BuildingPrompt,
            "launchingagentprocess" => RunAttemptStatus.LaunchingAgentProcess,
            "initializingsession" => RunAttemptStatus.InitializingSession,
            "streamingturn" => RunAttemptStatus.StreamingTurn,
            "finishing" => RunAttemptStatus.Finishing,
            "succeeded" => RunAttemptStatus.Succeeded,
            "failed" => RunAttemptStatus.Failed,
            "timedout" => RunAttemptStatus.TimedOut,
            "stalled" => RunAttemptStatus.Stalled,
            "canceledbyreconciliation" => RunAttemptStatus.CanceledByReconciliation,
            _ => null
        };
    }

    internal static string GetKindLabel(SessionActivityKind kind)
    {
        return kind switch
        {
            SessionActivityKind.LifecycleMilestone => "Lifecycle",
            SessionActivityKind.AgentMessage => "Agent message",
            SessionActivityKind.ProgressUpdate => "Progress",
            SessionActivityKind.Warning => "Warning",
            SessionActivityKind.Error => "Error",
            SessionActivityKind.Outcome => "Outcome",
            _ => "Activity"
        };
    }

    internal static Badge.BadgeColor GetKindBadgeColor(SessionActivityKind kind)
    {
        return kind switch
        {
            SessionActivityKind.AgentMessage => Badge.BadgeColor.Info,
            SessionActivityKind.Warning => Badge.BadgeColor.Warning,
            SessionActivityKind.Error => Badge.BadgeColor.Failure,
            SessionActivityKind.Outcome => Badge.BadgeColor.Success,
            _ => Badge.BadgeColor.Gray
        };
    }

    internal static TimelineColor GetTimelineColor(SessionActivityEntry entry)
    {
        return entry.Kind switch
        {
            SessionActivityKind.LifecycleMilestone => TimelineColor.Gray,
            SessionActivityKind.AgentMessage => TimelineColor.Blue,
            SessionActivityKind.ProgressUpdate => TimelineColor.Gray,
            SessionActivityKind.Warning => TimelineColor.Orange,
            SessionActivityKind.Error => TimelineColor.Red,
            SessionActivityKind.Outcome => IsSuccessfulOutcome(entry) ? TimelineColor.Green : TimelineColor.Red,
            _ => TimelineColor.Gray
        };
    }

    private static bool IsSuccessfulOutcome(SessionActivityEntry entry)
    {
        var combined = $"{entry.Title} {entry.Detail}".Trim().ToLowerInvariant();
        return combined.Contains("succeeded", StringComparison.Ordinal)
            || combined.Contains("completed", StringComparison.Ordinal);
    }
}
