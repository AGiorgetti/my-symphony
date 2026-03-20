using System.Globalization;
using System.Text.Json;
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

    internal static string GetAttemptLabel(int? attempt, bool isKnown)
    {
        if (!isKnown)
        {
            return "Unavailable";
        }

        return attempt is null ? "Initial run" : $"Attempt {attempt}";
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

    internal static SessionActivityTimelineEntryModel CreateTimelineEntry(SessionActivityEntry entry)
    {
        var detail = NormalizeDetail(entry.Detail);
        var detailPresentation = BuildDetailPresentation(detail);

        return new SessionActivityTimelineEntryModel(
            entry.Kind,
            entry.Timestamp,
            FormatTimestamp(entry.Timestamp),
            entry.Title,
            GetKindLabel(entry.Kind),
            GetKindBadgeColor(entry.Kind),
            detailPresentation.Summary,
            detailPresentation.Detail,
            detailPresentation.DetailPreview,
            detailPresentation.HasExpandableDetail,
            detailPresentation.IsStructuredDetail,
            GetTimelineColor(entry));
    }

    internal static int? TryParseTurnCount(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        var markerIndex = sessionId.LastIndexOf("-turn-", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var turnSegment = sessionId[(markerIndex + 6)..];
        return int.TryParse(turnSegment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var turnCount)
            ? turnCount
            : null;
    }

    private static bool IsSuccessfulOutcome(SessionActivityEntry entry)
    {
        var combined = $"{entry.Title} {entry.Detail}".Trim().ToLowerInvariant();
        return combined.Contains("succeeded", StringComparison.Ordinal)
            || combined.Contains("completed", StringComparison.Ordinal);
    }

    private static string? NormalizeDetail(string? detail)
    {
        return string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
    }

    private static ActivityDetailPresentation BuildDetailPresentation(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return ActivityDetailPresentation.Empty;
        }

        if (TryFormatJson(detail, out var formattedJson, out var structuredSummary))
        {
            return new ActivityDetailPresentation(
                structuredSummary,
                formattedJson,
                "Expand payload",
                HasExpandableDetail: true,
                IsStructuredDetail: true);
        }

        var normalized = detail.ReplaceLineEndings("\n");
        var isMultiline = normalized.Contains('\n', StringComparison.Ordinal);
        var isLong = normalized.Length > 180;

        if (!isMultiline && !isLong)
        {
            return new ActivityDetailPresentation(
                Summary: null,
                Detail: normalized,
                DetailPreview: null,
                HasExpandableDetail: false,
                IsStructuredDetail: false);
        }

        var preview = TruncateText(FirstMeaningfulLine(normalized), 160);
        return new ActivityDetailPresentation(
            preview,
            normalized,
            "Expand detail",
            HasExpandableDetail: true,
            IsStructuredDetail: false);
    }

    private static bool TryFormatJson(string detail, out string formattedJson, out string summary)
    {
        formattedJson = string.Empty;
        summary = string.Empty;

        var trimmed = detail.Trim();
        if (!(trimmed.StartsWith('{') || trimmed.StartsWith('[')))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            formattedJson = JsonSerializer.Serialize(
                document.RootElement,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });
            summary = DescribeJson(document.RootElement);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string DescribeJson(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => DescribeObject(element),
            JsonValueKind.Array => DescribeArray(element),
            _ => "Structured payload"
        };
    }

    private static string DescribeObject(JsonElement element)
    {
        var propertyNames = element.EnumerateObject()
            .Select(property => property.Name)
            .Take(3)
            .ToArray();
        var propertyCount = element.EnumerateObject().Count();

        if (propertyCount == 0)
        {
            return "JSON object payload";
        }

        var preview = string.Join(", ", propertyNames);
        return propertyCount > propertyNames.Length
            ? $"JSON object payload with {propertyCount} properties: {preview}, ..."
            : $"JSON object payload with {propertyCount} properties: {preview}";
    }

    private static string DescribeArray(JsonElement element)
    {
        var count = element.GetArrayLength();
        return count == 1
            ? "JSON array payload with 1 item"
            : $"JSON array payload with {count} items";
    }

    private static string FirstMeaningfulLine(string detail)
    {
        foreach (var line in detail.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                return trimmed;
            }
        }

        return detail.Trim();
    }

    private static string TruncateText(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return $"{value[..(maxLength - 1)].TrimEnd()}…";
    }

    private sealed record ActivityDetailPresentation(
        string? Summary,
        string? Detail,
        string? DetailPreview,
        bool HasExpandableDetail,
        bool IsStructuredDetail)
    {
        public static ActivityDetailPresentation Empty { get; } = new(null, null, null, false, false);
    }
}
