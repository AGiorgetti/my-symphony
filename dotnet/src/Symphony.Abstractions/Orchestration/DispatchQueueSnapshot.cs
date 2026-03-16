namespace Symphony.Abstractions.Orchestration;

public sealed record DispatchQueueSnapshot(
    IReadOnlyList<QueuedDispatchSnapshot> Queued,
    IReadOnlyList<RunningDispatchSnapshot> Running,
    IReadOnlyList<RetryDispatchSnapshot> Retrying,
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
