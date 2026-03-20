using System.Globalization;
using Flowbite.Components;

namespace Symphony.Host.Components.Sessions;

internal static class SessionListDisplay
{
    internal static string FormatTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
    }

    internal static string FormatDuration(double durationSeconds)
    {
        return durationSeconds >= 60d
            ? $"{durationSeconds / 60d:0.#} min"
            : $"{durationSeconds:0.#} s";
    }

    internal static string GetDurationLabel(SessionListRowViewModel session)
    {
        var duration = FormatDuration(session.DurationSeconds);
        return session.IsActive ? $"{duration} live" : duration;
    }

    internal static Badge.BadgeColor GetStatusColor(SessionListRowViewModel session)
    {
        return session.Status.ToLowerInvariant() switch
        {
            "in progress" => Badge.BadgeColor.Info,
            "streaming" => Badge.BadgeColor.Info,
            "finishing" => Badge.BadgeColor.Info,
            "retrying" => Badge.BadgeColor.Warning,
            "succeeded" => Badge.BadgeColor.Success,
            "failed" => Badge.BadgeColor.Failure,
            "timedout" => Badge.BadgeColor.Failure,
            "stalled" => Badge.BadgeColor.Failure,
            "canceled" => Badge.BadgeColor.Gray,
            _ when session.IsActive => Badge.BadgeColor.Info,
            _ => Badge.BadgeColor.Gray
        };
    }
}
