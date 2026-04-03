using System.Text.Json;
using Symphony.Domain.Sessions;

namespace Symphony.Host.Dashboard;

internal static class SessionActivityTokenEstimator
{
    public static SessionActivityEntry Normalize(SessionActivityEntry activity)
    {
        if (activity.TokenUsage is not null
            && string.Equals(activity.TokenUsage.Source, "per-entry-estimate", StringComparison.Ordinal)
            && activity.TokenUsage.EstimatedTotalTokens > 0)
        {
            return activity;
        }

        var estimatedTokenUsage = TryCreatePerEntryEstimatedTokenSnapshot(activity);
        return activity with
        {
            TokenUsage = estimatedTokenUsage
        };
    }

    public static SessionActivityTokenSnapshot? TryCreatePerEntryEstimatedTokenSnapshot(SessionActivityEntry activity)
    {
        if (activity.Kind != SessionActivityKind.DebugMessage || string.IsNullOrWhiteSpace(activity.Detail))
        {
            return null;
        }

        var title = activity.Title.Trim();
        if (title.StartsWith("Sent turn/start", StringComparison.Ordinal))
        {
            var inputTokens = EstimateTurnStartTokens(activity.Detail);
            return inputTokens > 0
                ? CreatePerEntryEstimateSnapshot("turn/start", activity.Timestamp, inputTokens, 0)
                : null;
        }

        if (title.StartsWith("Received item/started", StringComparison.Ordinal))
        {
            var inputTokens = EstimateItemStartedTokens(activity.Detail);
            return inputTokens > 0
                ? CreatePerEntryEstimateSnapshot("item/started", activity.Timestamp, inputTokens, 0)
                : null;
        }

        if (title.StartsWith("Received item/completed", StringComparison.Ordinal))
        {
            var outputTokens = EstimateItemCompletedTokens(activity.Detail);
            return outputTokens > 0
                ? CreatePerEntryEstimateSnapshot("item/completed", activity.Timestamp, 0, outputTokens)
                : null;
        }

        return null;
    }

    private static SessionActivityTokenSnapshot CreatePerEntryEstimateSnapshot(
        string kind,
        DateTimeOffset timestamp,
        long inputTokens,
        long outputTokens)
    {
        var totalTokens = inputTokens + outputTokens;
        return new SessionActivityTokenSnapshot(
            "per-entry-estimate",
            EffectiveInputTokens: 0,
            EffectiveOutputTokens: 0,
            EffectiveTotalTokens: 0,
            EstimatedInputTokens: inputTokens,
            EstimatedOutputTokens: outputTokens,
            EstimatedTotalTokens: totalTokens,
            ReportedInputTokens: 0,
            ReportedCachedInputTokens: 0,
            ReportedOutputTokens: 0,
            ReportedReasoningTokens: 0,
            ReportedTotalTokens: 0,
            ComparisonStatus: SessionTokenComparisonStatus.None,
            InputDelta: 0,
            OutputDelta: 0,
            TotalDelta: 0,
            LastEstimatedAt: timestamp,
            LastReportedAt: null,
            LastOperation: new DashboardSessionTokenOperationSnapshot(
                $"{kind}:{timestamp:O}",
                kind,
                timestamp,
                TurnNumber: 0,
                InputTokens: inputTokens,
                CachedInputTokens: 0,
                OutputTokens: outputTokens,
                ReasoningTokens: 0,
                TotalTokens: totalTokens));
    }

    private static int EstimateTurnStartTokens(string payload)
    {
        return TryParsePayload(
            payload,
            static root =>
            {
                if (!TryGetNestedElement(root, ["params", "input"], out var inputItems)
                    || inputItems.ValueKind != JsonValueKind.Array)
                {
                    return 0;
                }

                var total = 0;
                foreach (var item in inputItems.EnumerateArray())
                {
                    total += EstimateTextTokensFromElement(item);
                }

                return total;
            });
    }

    private static int EstimateItemStartedTokens(string payload)
    {
        return TryParsePayload(
            payload,
            static root => TryGetNestedElement(root, ["params", "item"], out var item)
                ? EstimateTextTokensFromElement(item)
                : 0);
    }

    private static int EstimateItemCompletedTokens(string payload)
    {
        return TryParsePayload(
            payload,
            static root => TryGetNestedElement(root, ["params", "item"], out var item)
                ? EstimateTextTokensFromElement(item)
                : 0);
    }

    private static int TryParsePayload(string payload, Func<JsonElement, int> estimator)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return estimator(document.RootElement);
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static int EstimateTextTokensFromElement(JsonElement element)
    {
        var total = 0;

        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
            {
                total += EstimateTextTokens(textElement.GetString());
            }

            if (element.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in contentElement.EnumerateArray())
                {
                    total += EstimateTextTokensFromElement(item);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                total += EstimateTextTokensFromElement(item);
            }
        }

        return total;
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

    private static int EstimateTextTokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var trimmed = text.Trim();
        return Math.Max(1, (int)Math.Ceiling(trimmed.Length / 4d));
    }
}
