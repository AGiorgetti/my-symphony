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
    string? Detail);

public sealed record SessionRecord(
    string IssueIdentifier,
    string? IssueUrl,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string? FinalOutcome,
    string? FinalError,
    bool IsActive);
