using System.Text.Json.Serialization;
using Symphony.Abstractions.Orchestration;

namespace Symphony.Host.Api;

public sealed record StateResponseDto(
    [property: JsonPropertyName("generated_at")] DateTimeOffset GeneratedAt,
    [property: JsonPropertyName("counts")] StateCountsDto Counts,
    [property: JsonPropertyName("health")] HealthStatusDto Health,
    [property: JsonPropertyName("orchestration")] OrchestrationStatusDto Orchestration,
    [property: JsonPropertyName("running")] IReadOnlyList<RunningIssueDto> Running,
    [property: JsonPropertyName("retrying")] IReadOnlyList<RetryingIssueDto> Retrying,
    [property: JsonPropertyName("blocked")] IReadOnlyList<BlockedIssueDto> Blocked,
    [property: JsonPropertyName("codex_totals")] CodexTotalsDto CodexTotals,
    [property: JsonPropertyName("rate_limits")] object? RateLimits);

public sealed record StateCountsDto(
    [property: JsonPropertyName("running")] int Running,
    [property: JsonPropertyName("retrying")] int Retrying,
    [property: JsonPropertyName("blocked")] int Blocked);

public sealed record HealthStatusDto(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("orchestrator_state")] OrchestratorControlState OrchestratorState,
    [property: JsonPropertyName("orchestrator_state_changed_at")] DateTimeOffset OrchestratorStateChangedAt,
    [property: JsonPropertyName("last_poll_tick_at")] DateTimeOffset? LastPollTickAt,
    [property: JsonPropertyName("last_successful_poll_at")] DateTimeOffset? LastSuccessfulPollAt,
    [property: JsonPropertyName("last_successful_poll_age_seconds")] double? LastSuccessfulPollAgeSeconds,
    [property: JsonPropertyName("poll_is_stale")] bool PollIsStale,
    [property: JsonPropertyName("workflow_load_status")] string WorkflowLoadStatus,
    [property: JsonPropertyName("workflow_last_loaded_at")] DateTimeOffset? WorkflowLastLoadedAt,
    [property: JsonPropertyName("workflow_path")] string? WorkflowPath,
    [property: JsonPropertyName("poll_last_error")] string? PollLastError,
    [property: JsonPropertyName("workflow_last_error")] string? WorkflowLastError);

public sealed record RunningIssueDto(
    [property: JsonPropertyName("issue_id")] string IssueId,
    [property: JsonPropertyName("issue_identifier")] string IssueIdentifier,
    [property: JsonPropertyName("orchestrator_session_id")] string? OrchestratorSessionId,
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

public sealed record BlockedIssueDto(
    [property: JsonPropertyName("issue_id")] string IssueId,
    [property: JsonPropertyName("issue_identifier")] string IssueIdentifier,
    [property: JsonPropertyName("orchestrator_session_id")] string OrchestratorSessionId,
    [property: JsonPropertyName("attempt")] int? Attempt,
    [property: JsonPropertyName("blocked_at")] DateTimeOffset BlockedAt,
    [property: JsonPropertyName("reason_code")] string ReasonCode,
    [property: JsonPropertyName("error_message")] string ErrorMessage,
    [property: JsonPropertyName("required_user_action")] string RequiredUserAction,
    [property: JsonPropertyName("follow_up_action_id")] string FollowUpActionId);

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
    [property: JsonPropertyName("orchestrator_session_id")] string? OrchestratorSessionId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("workspace")] WorkspaceDto Workspace,
    [property: JsonPropertyName("attempts")] IssueAttemptsDto Attempts,
    [property: JsonPropertyName("running")] RunningIssueDto? Running,
    [property: JsonPropertyName("retry")] RetryingIssueDto? Retry,
    [property: JsonPropertyName("blocked")] BlockedIssueDto? Blocked,
    [property: JsonPropertyName("follow_up_actions")] IReadOnlyList<FollowUpActionDto> FollowUpActions,
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

public sealed record FollowUpActionDto(
    [property: JsonPropertyName("fai_id")] string FollowUpActionId,
    [property: JsonPropertyName("issue_identifier")] string IssueIdentifier,
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("reason_code")] string ReasonCode,
    [property: JsonPropertyName("error_message")] string ErrorMessage,
    [property: JsonPropertyName("required_user_action")] string RequiredUserAction,
    [property: JsonPropertyName("options")] IReadOnlyList<FollowUpActionOptionDto> Options,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("resolved_by")] string? ResolvedBy,
    [property: JsonPropertyName("resolved_at")] DateTimeOffset? ResolvedAt,
    [property: JsonPropertyName("selected_option_id")] string? SelectedOptionId,
    [property: JsonPropertyName("notes")] string? Notes);

public sealed record FollowUpActionOptionDto(
    [property: JsonPropertyName("option_id")] string OptionId,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("description")] string? Description);

public sealed record ResolveFollowUpActionRequestDto(
    [property: JsonPropertyName("selected_option_id")] string? SelectedOptionId,
    [property: JsonPropertyName("notes")] string? Notes);

public sealed record RefreshResponseDto(
    [property: JsonPropertyName("queued")] bool Queued,
    [property: JsonPropertyName("coalesced")] bool Coalesced,
    [property: JsonPropertyName("requested_at")] DateTimeOffset RequestedAt,
    [property: JsonPropertyName("operations")] IReadOnlyList<string> Operations);

public sealed record OrchestrationStatusDto(
    [property: JsonPropertyName("state")] OrchestratorControlState State,
    [property: JsonPropertyName("changed_at")] DateTimeOffset ChangedAt);

public sealed record ErrorEnvelopeDto(
    [property: JsonPropertyName("error")] ErrorDetailsDto Error);

public sealed record ErrorDetailsDto(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);
