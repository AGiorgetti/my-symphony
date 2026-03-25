using System.Globalization;
using MudBlazor;

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

    internal static Color GetStatusColor(SessionListRowViewModel session)
    {
        return session.Status.ToLowerInvariant() switch
        {
            "in progress" => Color.Info,
            "streaming" => Color.Info,
            "finishing" => Color.Info,
            "retrying" => Color.Warning,
            "succeeded" => Color.Success,
            "failed" => Color.Error,
            "timedout" => Color.Error,
            "stalled" => Color.Error,
            "canceled" => Color.Default,
            _ when session.IsActive => Color.Info,
            _ => Color.Default
        };
    }
}
