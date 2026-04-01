using Symphony.Domain.Runs;
using Symphony.Domain.Sessions;

namespace Symphony.Abstractions.Orchestration;

public sealed record ActiveSessionSnapshot(
    string IssueId,
    string IssueIdentifier,
    string OrchestratorSessionId,
    string IssueState,
    int? Attempt,
    DateTimeOffset StartedAt,
    RunAttemptStatus Status,
    string? Error,
    LiveSessionMetadata? Session);
