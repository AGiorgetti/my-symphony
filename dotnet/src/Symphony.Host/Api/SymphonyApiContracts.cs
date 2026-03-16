using System.Text.Json.Serialization;

namespace Symphony.Host.Api;

public sealed record StateResponseDto(
    [property: JsonPropertyName("generated_at")] DateTimeOffset GeneratedAt,
    [property: JsonPropertyName("counts")] StateCountsDto Counts,
    [property: JsonPropertyName("running")] IReadOnlyList<RunningIssueDto> Running,
    [property: JsonPropertyName("retrying")] IReadOnlyList<RetryingIssueDto> Retrying,
    [property: JsonPropertyName("codex_totals")] CodexTotalsDto CodexTotals,
    [property: JsonPropertyName("rate_limits")] object? RateLimits);

public sealed record StateCountsDto(
    [property: JsonPropertyName("running")] int Running,
    [property: JsonPropertyName("retrying")] int Retrying);

public sealed record RunningIssueDto(
    [property: JsonPropertyName("issue_id")] string IssueId,
    [property: JsonPropertyName("issue_identifier")] string IssueIdentifier,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("turn_count")] int TurnCount,
    [property: JsonPropertyName("last_event")] string? LastEvent,
    [property: JsonPropertyName("last_message")] string? LastMessage,
    [property: JsonPropertyName("started_at")] DateTimeOffset StartedAt,
    [property: JsonPropertyName("last_event_at")] DateTimeOffset? LastEventAt,
    [property: JsonPropertyName("tokens")] TokenTotalsDto Tokens);

public sealed record RetryingIssueDto(
    [property: JsonPropertyName("issue_id")] string IssueId,
    [property: JsonPropertyName("issue_identifier")] string IssueIdentifier,
    [property: JsonPropertyName("attempt")] int Attempt,
    [property: JsonPropertyName("due_at")] DateTimeOffset DueAt,
    [property: JsonPropertyName("error")] string? Error);

public sealed record TokenTotalsDto(
    [property: JsonPropertyName("input_tokens")] long InputTokens,
    [property: JsonPropertyName("output_tokens")] long OutputTokens,
    [property: JsonPropertyName("total_tokens")] long TotalTokens);

public sealed record CodexTotalsDto(
    [property: JsonPropertyName("input_tokens")] long InputTokens,
    [property: JsonPropertyName("output_tokens")] long OutputTokens,
    [property: JsonPropertyName("total_tokens")] long TotalTokens,
    [property: JsonPropertyName("seconds_running")] double SecondsRunning);

public sealed record IssueResponseDto(
    [property: JsonPropertyName("issue_identifier")] string IssueIdentifier,
    [property: JsonPropertyName("issue_id")] string IssueId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("workspace")] WorkspaceDto Workspace,
    [property: JsonPropertyName("attempts")] IssueAttemptsDto Attempts,
    [property: JsonPropertyName("running")] RunningIssueDto? Running,
    [property: JsonPropertyName("retry")] RetryingIssueDto? Retry,
    [property: JsonPropertyName("logs")] IssueLogsDto Logs,
    [property: JsonPropertyName("recent_events")] IReadOnlyList<RuntimeEventDto> RecentEvents,
    [property: JsonPropertyName("last_error")] string? LastError,
    [property: JsonPropertyName("tracked")] IReadOnlyDictionary<string, object?> Tracked);

public sealed record WorkspaceDto(
    [property: JsonPropertyName("path")] string Path);

public sealed record IssueAttemptsDto(
    [property: JsonPropertyName("restart_count")] int RestartCount,
    [property: JsonPropertyName("current_retry_attempt")] int? CurrentRetryAttempt);

public sealed record IssueLogsDto(
    [property: JsonPropertyName("codex_session_logs")] IReadOnlyList<SessionLogDto> CodexSessionLogs);

public sealed record SessionLogDto(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("path")] string? Path,
    [property: JsonPropertyName("url")] string? Url);

public sealed record RuntimeEventDto(
    [property: JsonPropertyName("at")] DateTimeOffset At,
    [property: JsonPropertyName("event")] string? Event,
    [property: JsonPropertyName("message")] string? Message);

public sealed record RefreshResponseDto(
    [property: JsonPropertyName("queued")] bool Queued,
    [property: JsonPropertyName("coalesced")] bool Coalesced,
    [property: JsonPropertyName("requested_at")] DateTimeOffset RequestedAt,
    [property: JsonPropertyName("operations")] IReadOnlyList<string> Operations);

public sealed record ErrorEnvelopeDto(
    [property: JsonPropertyName("error")] ErrorDetailsDto Error);

public sealed record ErrorDetailsDto(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);
