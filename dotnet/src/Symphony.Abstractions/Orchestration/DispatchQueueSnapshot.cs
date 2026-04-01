namespace Symphony.Abstractions.Orchestration;

public sealed record DispatchQueueSnapshot(
    IReadOnlyList<QueuedDispatchSnapshot> Queued,
    IReadOnlyList<RunningDispatchSnapshot> Running,
    IReadOnlyList<RetryDispatchSnapshot> Retrying,
    IReadOnlyList<BlockedDispatchSnapshot> Blocked,
    int MaxConcurrentAgents,
    int AvailableSlots);

public sealed record QueuedDispatchSnapshot(
    string IssueId,
    string IssueIdentifier,
    string IssueState,
    int? Attempt,
    DateTimeOffset QueuedAt);

public sealed record RunningDispatchSnapshot(
    string IssueId,
    string IssueIdentifier,
    string IssueState,
    int? Attempt,
    DateTimeOffset StartedAt);

public sealed record RetryDispatchSnapshot(
    string IssueId,
    string IssueIdentifier,
    int Attempt,
    DateTimeOffset DueAt,
    string? Error);

public sealed record BlockedDispatchSnapshot(
    string IssueId,
    string IssueIdentifier,
    string OrchestratorSessionId,
    int? Attempt,
    DateTimeOffset BlockedAt,
    BlockingReasonCode ReasonCode,
    string ErrorMessage,
    string RequiredUserAction,
    string FollowUpActionId);

public sealed record FollowUpActionSnapshot(
    string FollowUpActionId,
    string IssueId,
    string IssueIdentifier,
    string SessionId,
    DateTimeOffset CreatedAt,
    BlockingReasonCode ReasonCode,
    string ErrorMessage,
    string RequiredUserAction,
    IReadOnlyList<FollowUpActionOptionSnapshot> Options,
    FollowUpActionStatus Status,
    string? ResolvedBy,
    DateTimeOffset? ResolvedAt,
    string? SelectedOptionId,
    string? Notes);

public sealed record FollowUpActionOptionSnapshot(
    string OptionId,
    string Label,
    string? Description);

public enum BlockingReasonCode
{
    InputRequired,
    ApprovalRequired,
    ManualDecisionRequired,
    ToolUnavailable,
    PreconditionFailed
}

public enum FollowUpActionStatus
{
    Pending,
    Resolved,
    Expired
}
