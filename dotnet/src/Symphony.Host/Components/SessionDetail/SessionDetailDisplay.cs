using System.Globalization;
using System.Text.Json;
using MudBlazor;
using Symphony.Domain.Runs;
using Symphony.Domain.Sessions;
using Symphony.Host.Dashboard;

namespace Symphony.Host.Components.SessionDetail;

internal static class SessionDetailDisplay
{
    internal static string FormatTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
    }

    internal static string FormatCompactTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.ToUniversalTime().ToString("HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
    }

    internal static Color GetFallbackStatusColor(string statusText, bool isActive)
    {
        if (isActive)
        {
            return Color.Info;
        }

        return statusText.Trim().ToLowerInvariant() switch
        {
            "succeeded" => Color.Success,
            "needs attention" => Color.Warning,
            "blocked_error" => Color.Warning,
            "blockederror" => Color.Warning,
            "retrying" => Color.Warning,
            "failed" => Color.Error,
            "timedout" => Color.Error,
            "stalled" => Color.Error,
            "canceled" => Color.Default,
            "canceledbyreconciliation" => Color.Default,
            _ => Color.Default
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
            SessionActivityKind.DebugMessage => "Debug transcript",
            SessionActivityKind.ProgressUpdate => "Progress",
            SessionActivityKind.AttentionRequired => "Attention",
            SessionActivityKind.Warning => "Warning",
            SessionActivityKind.Error => "Error",
            SessionActivityKind.Outcome => "Outcome",
            _ => "Activity"
        };
    }

    internal static Color GetKindBadgeColor(SessionActivityKind kind)
    {
        return kind switch
        {
            SessionActivityKind.AgentMessage => Color.Info,
            SessionActivityKind.DebugMessage => Color.Info,
            SessionActivityKind.AttentionRequired => Color.Warning,
            SessionActivityKind.Warning => Color.Warning,
            SessionActivityKind.Error => Color.Error,
            SessionActivityKind.Outcome => Color.Success,
            _ => Color.Default
        };
    }

    internal static Color GetTimelineColor(SessionActivityEntry entry)
    {
        return entry.Kind switch
        {
            SessionActivityKind.LifecycleMilestone => Color.Default,
            SessionActivityKind.AgentMessage => Color.Info,
            SessionActivityKind.DebugMessage => Color.Info,
            SessionActivityKind.ProgressUpdate => Color.Default,
            SessionActivityKind.AttentionRequired => Color.Warning,
            SessionActivityKind.Warning => Color.Warning,
            SessionActivityKind.Error => Color.Error,
            SessionActivityKind.Outcome => IsSuccessfulOutcome(entry) ? Color.Success : Color.Error,
            _ => Color.Default
        };
    }

    internal static SessionActivityTimelineEntryModel CreateTimelineEntry(SessionActivityEntry entry)
    {
        var detail = NormalizeDetail(entry.Detail);
        var detailPresentation = BuildDetailPresentation(detail);
        var displayTitle = HumanizeTitle(entry.Title);
        var hasVisibleTokenUsage = HasVisibleEntryTokenUsage(entry.TokenUsage);
        var facts = MergeFacts(detailPresentation.Facts, entry.TokenUsage);
        var methodTag = TryGetMethodTag(facts);
        var compactPreview = BuildCompactPreview(detailPresentation.Summary, detailPresentation.DetailPreview, entry.Detail);
        var isEmptyAgentMessage = entry.Kind == SessionActivityKind.AgentMessage
            && string.IsNullOrWhiteSpace(compactPreview)
            && string.IsNullOrWhiteSpace(detailPresentation.Detail)
            && facts.Count == 0;

        return new SessionActivityTimelineEntryModel(
            entry.Kind,
            entry.Timestamp,
            FormatTimestamp(entry.Timestamp),
            displayTitle,
            GetKindLabel(entry.Kind),
            GetKindBadgeColor(entry.Kind),
            detailPresentation.Summary,
            facts,
            detailPresentation.Detail,
            detailPresentation.DetailPreview,
            detailPresentation.DetailToggleLabel,
            detailPresentation.HasExpandableDetail,
            detailPresentation.IsStructuredDetail,
            GetTimelineColor(entry),
            isEmptyAgentMessage,
            hasVisibleTokenUsage,
            GetTokenSourceLabel(entry.TokenUsage),
            FormatCompactTimestamp(entry.Timestamp),
            compactPreview,
            methodTag);
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

    private static string HumanizeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "Activity";
        }

        var trimmed = title.Trim();
        if (!trimmed.Contains('_', StringComparison.Ordinal) && !trimmed.Contains('-', StringComparison.Ordinal))
        {
            return trimmed;
        }

        var normalized = trimmed.Replace('_', ' ').Replace('-', ' ');
        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return trimmed;
        }

        for (var index = 0; index < parts.Length; index++)
        {
            parts[index] = char.ToUpperInvariant(parts[index][0]) + parts[index][1..].ToLowerInvariant();
        }

        return string.Join(' ', parts);
    }

    private static string? TryGetMethodTag(IReadOnlyList<SessionActivityFactModel> facts)
    {
        return facts.FirstOrDefault(candidate => string.Equals(candidate.Label, "Method", StringComparison.Ordinal))?.Value;
    }

    private static string? BuildCompactPreview(string? summary, string? detailPreview, string? rawDetail)
    {
        var candidate = !string.IsNullOrWhiteSpace(summary)
            ? summary
            : !string.IsNullOrWhiteSpace(detailPreview)
                ? detailPreview
                : NormalizeDetail(rawDetail);

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        return TruncateText(FirstMeaningfulLine(candidate), 84);
    }

    private static ActivityDetailPresentation BuildDetailPresentation(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return ActivityDetailPresentation.Empty;
        }

        if (TryFormatJson(detail, out var formattedJson, out var structuredSummary, out var facts))
        {
            return new ActivityDetailPresentation(
                "Structured event payload",
                facts,
                formattedJson,
                structuredSummary,
                "View structured payload",
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
                Facts: Array.Empty<SessionActivityFactModel>(),
                Detail: normalized,
                DetailPreview: null,
                DetailToggleLabel: null,
                HasExpandableDetail: false,
                IsStructuredDetail: false);
        }

        var preview = TruncateText(FirstMeaningfulLine(normalized), 160);
        return new ActivityDetailPresentation(
            preview,
            Array.Empty<SessionActivityFactModel>(),
            normalized,
            preview,
            "View full detail",
            HasExpandableDetail: true,
            IsStructuredDetail: false);
    }

    private static bool TryFormatJson(
        string detail,
        out string formattedJson,
        out string summary,
        out IReadOnlyList<SessionActivityFactModel> facts)
    {
        formattedJson = string.Empty;
        summary = string.Empty;
        facts = Array.Empty<SessionActivityFactModel>();

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
            facts = ExtractFacts(document.RootElement);
            summary = DescribeJson(document.RootElement, facts);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string DescribeJson(JsonElement element, IReadOnlyList<SessionActivityFactModel> facts)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => DescribeObject(element, facts),
            JsonValueKind.Array => DescribeArray(element),
            _ => "Structured payload"
        };
    }

    private static string DescribeObject(JsonElement element, IReadOnlyList<SessionActivityFactModel> facts)
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

        if (facts.Count > 0)
        {
            var keyFacts = string.Join(" | ", facts.Take(3).Select(fact => $"{fact.Label}: {fact.Value}"));
            return propertyCount > 3
                ? $"{keyFacts} | {propertyCount} fields"
                : keyFacts;
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

        return $"{value[..(maxLength - 1)].TrimEnd()}...";
    }

    private static IReadOnlyList<SessionActivityFactModel> ExtractFacts(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return
            [
                new SessionActivityFactModel("Items", element.GetArrayLength().ToString(CultureInfo.InvariantCulture))
            ];
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<SessionActivityFactModel>();
        }

        var facts = new List<SessionActivityFactModel>();

        if (TryGetString(element, "event", out var eventName))
        {
            facts.Add(new SessionActivityFactModel("Event", eventName));
        }

        if (TryGetString(element, "method", out var method))
        {
            facts.Add(new SessionActivityFactModel("Method", method));
        }

        if (TryGetScalar(element, "id", out var id))
        {
            facts.Add(new SessionActivityFactModel("Id", id));
        }

        if (TryGetNestedString(element, ["params", "threadId"], out var paramsThreadId)
            || TryGetNestedString(element, ["result", "thread", "id"], out paramsThreadId))
        {
            facts.Add(new SessionActivityFactModel("Thread", paramsThreadId));
        }

        if (TryGetNestedString(element, ["params", "turnId"], out var paramsTurnId)
            || TryGetNestedString(element, ["result", "turn", "id"], out paramsTurnId))
        {
            facts.Add(new SessionActivityFactModel("Turn", paramsTurnId));
        }

        if (TryGetFirstInputText(element, out var promptText))
        {
            facts.Add(new SessionActivityFactModel("Prompt", TruncateText(promptText, 80)));
        }

        if (element.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
        {
            var fileNames = files.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Take(2)
                .Cast<string>()
                .ToArray();

            if (fileNames.Length > 0)
            {
                var value = files.GetArrayLength() > fileNames.Length
                    ? $"{string.Join(", ", fileNames)}, +{files.GetArrayLength() - fileNames.Length}"
                    : string.Join(", ", fileNames);
                facts.Add(new SessionActivityFactModel("Files", value));
            }
        }

        if (element.TryGetProperty("stats", out var stats) && stats.ValueKind == JsonValueKind.Object)
        {
            if (TryGetNumeric(stats, "input", out var input))
            {
                facts.Add(new SessionActivityFactModel("Input", input));
            }

            if (TryGetNumeric(stats, "cachedInput", out var cachedInput))
            {
                facts.Add(new SessionActivityFactModel("Cached input", cachedInput));
            }

            if (TryGetNumeric(stats, "output", out var output))
            {
                facts.Add(new SessionActivityFactModel("Output", output));
            }

            if (TryGetNumeric(stats, "reasoning", out var reasoning))
            {
                facts.Add(new SessionActivityFactModel("Reasoning", reasoning));
            }

            if (TryGetNumeric(stats, "total", out var total))
            {
                facts.Add(new SessionActivityFactModel("Total", total));
            }
        }

        if (TryGetString(element, "source", out var source))
        {
            facts.Add(new SessionActivityFactModel("Source", source));
        }

        AddTokenUsageFacts(facts, element, "effective", "Current");
        AddTokenUsageFacts(facts, element, "estimated", "Estimated");
        AddTokenUsageFacts(facts, element, "reported", "Reported");
        AddOperationFacts(facts, element);
        AddComparisonFacts(facts, element);

        if (TryGetString(element, "error", out var error))
        {
            facts.Add(new SessionActivityFactModel("Error", TruncateText(error, 80)));
        }
        else if (TryGetString(element, "message", out var message))
        {
            facts.Add(new SessionActivityFactModel("Message", TruncateText(message, 80)));
        }

        return facts;
    }

    private static IReadOnlyList<SessionActivityFactModel> MergeFacts(
        IReadOnlyList<SessionActivityFactModel> existingFacts,
        SessionActivityTokenSnapshot? tokenUsage)
    {
        if (!HasVisibleEntryTokenUsage(tokenUsage))
        {
            return existingFacts;
        }

        var visibleTokenUsage = tokenUsage!;
        var facts = new List<SessionActivityFactModel>(existingFacts);

        AddFactIfMissing(facts, "Token source", GetTokenSourceLabel(visibleTokenUsage));
        AddFactIfMissing(facts, "Reported input", visibleTokenUsage.ReportedInputTokens.ToString(CultureInfo.InvariantCulture));
        if (visibleTokenUsage.ReportedCachedInputTokens > 0)
        {
            AddFactIfMissing(facts, "Cached input", visibleTokenUsage.ReportedCachedInputTokens.ToString(CultureInfo.InvariantCulture));
        }
        AddFactIfMissing(facts, "Reported output", visibleTokenUsage.ReportedOutputTokens.ToString(CultureInfo.InvariantCulture));
        if (visibleTokenUsage.ReportedReasoningTokens > 0)
        {
            AddFactIfMissing(facts, "Reasoning", visibleTokenUsage.ReportedReasoningTokens.ToString(CultureInfo.InvariantCulture));
        }
        AddFactIfMissing(facts, "Reported total", visibleTokenUsage.ReportedTotalTokens.ToString(CultureInfo.InvariantCulture));

        return facts;
    }

    private static void AddFactIfMissing(ICollection<SessionActivityFactModel> facts, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || facts.Any(candidate => string.Equals(candidate.Label, label, StringComparison.Ordinal)))
        {
            return;
        }

        facts.Add(new SessionActivityFactModel(label, value));
    }

    private static string? GetTokenSourceLabel(SessionActivityTokenSnapshot? tokenUsage)
    {
        if (!HasVisibleEntryTokenUsage(tokenUsage))
        {
            return null;
        }

        return tokenUsage!.Source switch
        {
            "thread-token-usage" => "Reported",
            _ => "Available"
        };
    }

    private static bool HasVisibleEntryTokenUsage(SessionActivityTokenSnapshot? tokenUsage)
    {
        return tokenUsage is not null
            && string.Equals(tokenUsage.Source, "thread-token-usage", StringComparison.Ordinal)
            && tokenUsage.ReportedTotalTokens > 0;
    }

    private static void AddTokenUsageFacts(
        ICollection<SessionActivityFactModel> facts,
        JsonElement element,
        string propertyName,
        string labelPrefix)
    {
        if (!element.TryGetProperty(propertyName, out var tokenGroup) || tokenGroup.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (TryGetNumeric(tokenGroup, "inputTokens", out var input))
        {
            facts.Add(new SessionActivityFactModel($"{labelPrefix} input", input));
        }

        if (TryGetNumeric(tokenGroup, "cachedInputTokens", out var cachedInput))
        {
            facts.Add(new SessionActivityFactModel($"{labelPrefix} cached", cachedInput));
        }

        if (TryGetNumeric(tokenGroup, "outputTokens", out var output))
        {
            facts.Add(new SessionActivityFactModel($"{labelPrefix} output", output));
        }

        if (TryGetNumeric(tokenGroup, "reasoningTokens", out var reasoning))
        {
            facts.Add(new SessionActivityFactModel($"{labelPrefix} reasoning", reasoning));
        }

        if (TryGetNumeric(tokenGroup, "totalTokens", out var total))
        {
            facts.Add(new SessionActivityFactModel($"{labelPrefix} total", total));
        }
    }

    private static void AddOperationFacts(ICollection<SessionActivityFactModel> facts, JsonElement element)
    {
        if (!element.TryGetProperty("operation", out var operation) || operation.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (TryGetNumeric(operation, "turnNumber", out var turnNumber))
        {
            facts.Add(new SessionActivityFactModel("Turn", turnNumber));
        }

        if (TryGetString(operation, "kind", out var kind))
        {
            facts.Add(new SessionActivityFactModel("Kind", kind));
        }
    }

    private static void AddComparisonFacts(ICollection<SessionActivityFactModel> facts, JsonElement element)
    {
        if (!element.TryGetProperty("comparison", out var comparison) || comparison.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (TryGetScalar(comparison, "status", out var status))
        {
            facts.Add(new SessionActivityFactModel("Comparison", status));
        }

        if (TryGetScalar(comparison, "totalDelta", out var totalDelta))
        {
            facts.Add(new SessionActivityFactModel("Total delta", totalDelta));
        }
    }

    private static bool TryGetScalar(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;

        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        value = property.ValueKind switch
        {
            JsonValueKind.String when !string.IsNullOrWhiteSpace(property.GetString()) => property.GetString()!,
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => bool.TrueString.ToLowerInvariant(),
            JsonValueKind.False => bool.FalseString.ToLowerInvariant(),
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;

        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetNestedString(JsonElement element, IReadOnlyList<string> path, out string value)
    {
        value = string.Empty;
        var current = element;

        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return false;
            }
        }

        if (current.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = current.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetFirstInputText(JsonElement element, out string value)
    {
        value = string.Empty;

        if (!TryGetNestedElement(element, ["params", "input"], out var inputItems)
            || inputItems.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in inputItems.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (TryGetString(item, "text", out value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetNestedElement(JsonElement element, IReadOnlyList<string> path, out JsonElement value)
    {
        value = element;
        var current = element;

        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return false;
            }
        }

        value = current;
        return true;
    }

    private static bool TryGetNumeric(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;

        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        value = property.ValueKind switch
        {
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.String when !string.IsNullOrWhiteSpace(property.GetString()) => property.GetString()!,
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(value);
    }

    private sealed record ActivityDetailPresentation(
        string? Summary,
        IReadOnlyList<SessionActivityFactModel> Facts,
        string? Detail,
        string? DetailPreview,
        string? DetailToggleLabel,
        bool HasExpandableDetail,
        bool IsStructuredDetail)
    {
        public static ActivityDetailPresentation Empty { get; } = new(
            null,
            Array.Empty<SessionActivityFactModel>(),
            null,
            null,
            null,
            false,
            false);
    }
}
