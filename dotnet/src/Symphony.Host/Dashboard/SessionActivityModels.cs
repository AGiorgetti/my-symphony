using Symphony.Domain.Sessions;

namespace Symphony.Host.Dashboard;

public enum SessionActivityKind
{
    LifecycleMilestone,
    AgentMessage,
    DebugMessage,
    ProgressUpdate,
    AttentionRequired,
    Warning,
    Error,
    Outcome
}

public sealed record SessionActivityEntry(
    SessionActivityKind Kind,
    DateTimeOffset Timestamp,
    string Title,
    string? Detail,
    SessionActivityTokenSnapshot? TokenUsage = null);

public sealed record SessionActivityTokenSnapshot(
    string Source,
    long EffectiveInputTokens,
    long EffectiveOutputTokens,
    long EffectiveTotalTokens,
    long EstimatedInputTokens,
    long EstimatedOutputTokens,
    long EstimatedTotalTokens,
    long ReportedInputTokens,
    long ReportedCachedInputTokens,
    long ReportedOutputTokens,
    long ReportedReasoningTokens,
    long ReportedTotalTokens,
    SessionTokenComparisonStatus ComparisonStatus,
    long InputDelta,
    long OutputDelta,
    long TotalDelta,
    DateTimeOffset? LastEstimatedAt,
    DateTimeOffset? LastReportedAt,
    DashboardSessionTokenOperationSnapshot? LastOperation = null);

public sealed record SessionRecord(
    string IssueIdentifier,
    string? IssueUrl,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string? FinalOutcome,
    string? FinalError,
    bool IsActive);
