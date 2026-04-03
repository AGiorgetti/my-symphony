using System.Text.Json;
using Symphony.Domain.Sessions;

namespace Symphony.Host.Dashboard;

internal static class SessionActivityTokenAnnotator
{
    public static SessionActivityEntry Normalize(SessionActivityEntry activity)
    {
        return activity with
        {
            TokenUsage = TryCreateThreadUsageSnapshot(activity)
        };
    }

    private static SessionActivityTokenSnapshot? TryCreateThreadUsageSnapshot(SessionActivityEntry activity)
    {
        if (activity.Kind != SessionActivityKind.DebugMessage
            || string.IsNullOrWhiteSpace(activity.Detail)
            || !activity.Title.StartsWith("Received thread/tokenUsage/updated", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(activity.Detail);
            var root = document.RootElement;
            if (!TryGetNestedElement(root, ["params", "tokenUsage", "total"], out var totalUsage)
                || totalUsage.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var totalInput = ReadNumeric(totalUsage, "input_tokens", "inputTokens");
            var totalCachedInput = ReadNumeric(totalUsage, "cached_tokens", "cachedTokens", "cachedInputTokens");
            var totalOutput = ReadNumeric(totalUsage, "output_tokens", "outputTokens");
            var totalReasoning = ReadNumeric(totalUsage, "reasoning_tokens", "reasoningTokens", "reasoningOutputTokens");
            var totalTokens = ReadNumeric(totalUsage, "total_tokens", "totalTokens");

            DashboardSessionTokenOperationSnapshot? lastOperation = null;
            if (TryGetNestedElement(root, ["params", "tokenUsage", "last"], out var lastUsage)
                && lastUsage.ValueKind == JsonValueKind.Object)
            {
                var lastInput = ReadNumeric(lastUsage, "input_tokens", "inputTokens");
                var lastCachedInput = ReadNumeric(lastUsage, "cached_tokens", "cachedTokens", "cachedInputTokens");
                var lastOutput = ReadNumeric(lastUsage, "output_tokens", "outputTokens");
                var lastReasoning = ReadNumeric(lastUsage, "reasoning_tokens", "reasoningTokens", "reasoningOutputTokens");
                var lastTotal = ReadNumeric(lastUsage, "total_tokens", "totalTokens");

                if (lastInput > 0 || lastCachedInput > 0 || lastOutput > 0 || lastReasoning > 0 || lastTotal > 0)
                {
                    var turnNumber = ExtractTurnNumber(root);
                    lastOperation = new DashboardSessionTokenOperationSnapshot(
                        $"thread/tokenUsage/updated:{activity.Timestamp:O}",
                        "thread/tokenUsage/updated",
                        activity.Timestamp,
                        turnNumber,
                        lastInput,
                        lastCachedInput,
                        lastOutput,
                        lastReasoning,
                        lastTotal);
                }
            }

            if (totalInput <= 0 && totalCachedInput <= 0 && totalOutput <= 0 && totalReasoning <= 0 && totalTokens <= 0)
            {
                return null;
            }

            return new SessionActivityTokenSnapshot(
                "thread-token-usage",
                EffectiveInputTokens: totalInput,
                EffectiveOutputTokens: totalOutput,
                EffectiveTotalTokens: totalTokens,
                EstimatedInputTokens: 0,
                EstimatedOutputTokens: 0,
                EstimatedTotalTokens: 0,
                ReportedInputTokens: totalInput,
                ReportedCachedInputTokens: totalCachedInput,
                ReportedOutputTokens: totalOutput,
                ReportedReasoningTokens: totalReasoning,
                ReportedTotalTokens: totalTokens,
                ComparisonStatus: SessionTokenComparisonStatus.ReportedOnly,
                InputDelta: 0,
                OutputDelta: 0,
                TotalDelta: 0,
                LastEstimatedAt: null,
                LastReportedAt: activity.Timestamp,
                LastOperation: lastOperation);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int ExtractTurnNumber(JsonElement root)
    {
        if (!TryGetNestedElement(root, ["params", "turnId"], out var turnIdElement)
            || turnIdElement.ValueKind != JsonValueKind.String)
        {
            return 0;
        }

        var turnId = turnIdElement.GetString();
        if (string.IsNullOrWhiteSpace(turnId))
        {
            return 0;
        }

        var markerIndex = turnId.LastIndexOf("-turn-", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return 0;
        }

        return int.TryParse(turnId[(markerIndex + 6)..], out var turnNumber)
            ? turnNumber
            : 0;
    }

    private static int ReadNumeric(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (element.TryGetProperty(propertyName, out var property))
            {
                if (property.TryGetInt32(out var intValue))
                {
                    return intValue;
                }

                if (property.ValueKind == JsonValueKind.String
                    && int.TryParse(property.GetString(), out var parsed))
                {
                    return parsed;
                }
            }
        }

        return 0;
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
}
