using Symphony.Abstractions.Orchestration;

namespace Symphony.Host.Dashboard;

public sealed record DashboardSnapshot(
    DateTimeOffset GeneratedAt,
    string ServiceHealth,
    string OrchestratorMode,
    OrchestratorControlState OrchestratorState,
    DateTimeOffset OrchestratorStateChangedAt,
    DateTimeOffset? LastPollTickAt,
    DateTimeOffset? LastSuccessfulPollAt,
    double? LastSuccessfulPollAgeSeconds,
    string WorkflowLoadStatus,
    DateTimeOffset? WorkflowLastLoadedAt,
    int RunningCount,
    int RetryingCount,
    long InputTokens,
    long OutputTokens,
    long TotalTokens,
    double SecondsRunning,
    IReadOnlyList<DashboardActiveSessionSnapshot> ActiveSessions,
    IReadOnlyList<DashboardRetrySnapshot> RetryQueue,
    IReadOnlyList<DashboardRecentAttemptSnapshot> RecentAttempts,
    string? LastError,
    string? WorkflowLastError,
    bool RequireExecMarker = false,
    string ExecMarker = "exec:agent");

public sealed record DashboardActiveSessionSnapshot(
    string IssueIdentifier,
    string State,
    string? SessionId,
    int TurnCount,
    string? LastEvent,
    string? LastMessage,
    DateTimeOffset StartedAt,
    DateTimeOffset? LastEventAt,
    long TotalTokens);

public sealed record DashboardRetrySnapshot(
    string IssueIdentifier,
    int Attempt,
    DateTimeOffset DueAt,
    string? Error);

public sealed record DashboardRecentAttemptSnapshot(
    string IssueIdentifier,
    int? Attempt,
    string Outcome,
    DateTimeOffset CompletedAt,
    double DurationSeconds,
    string? Error,
    string? SessionId);
