using Symphony.Abstractions.Orchestration;

namespace Symphony.Application.Runtime;

public sealed record OrchestratorStateSnapshot(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<RunningIssueSnapshot> Running,
    IReadOnlyList<RetryDispatchSnapshot> Retrying,
    CodexTotalsSnapshot CodexTotals,
    object? RateLimits,
    IReadOnlyList<BlockedDispatchSnapshot>? Blocked = null,
    IReadOnlyList<FollowUpActionSnapshot>? FollowUpActions = null);

public sealed record RunningIssueSnapshot(
    string IssueId,
    string IssueIdentifier,
    string State,
    string? SessionId,
    int TurnCount,
    string? LastEvent,
    string? LastMessage,
    DateTimeOffset StartedAt,
    DateTimeOffset? LastEventAt,
    long InputTokens,
    long OutputTokens,
    long TotalTokens,
    string? OrchestratorSessionId = null);

public sealed record CodexTotalsSnapshot(
    long InputTokens,
    long OutputTokens,
    long TotalTokens,
    double SecondsRunning);

public sealed record OrchestratorIssueSnapshot(
    string IssueIdentifier,
    string IssueId,
    string Status,
    int RestartCount,
    int? CurrentRetryAttempt,
    RunningIssueSnapshot? Running,
    RetryDispatchSnapshot? Retry,
    string? LastError,
    IReadOnlyList<RuntimeEventSnapshot> RecentEvents,
    string? OrchestratorSessionId = null,
    BlockedDispatchSnapshot? Blocked = null,
    IReadOnlyList<FollowUpActionSnapshot>? FollowUpActions = null);

public sealed record RuntimeEventSnapshot(
    DateTimeOffset At,
    string? Event,
    string? Message);
