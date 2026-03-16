namespace Symphony.Application.Configuration;

public sealed record WorkflowServiceOptions(
    WorkflowTrackerOptions Tracker,
    WorkflowPollingOptions Polling,
    WorkflowWorkspaceOptions Workspace,
    WorkflowHookOptions Hooks,
    WorkflowAgentOptions Agent,
    WorkflowCodexOptions Codex);

public sealed record WorkflowTrackerOptions(
    string Kind,
    string Endpoint,
    string ApiKey,
    string? ProjectSlug,
    string? Repository,
    string? Organization,
    string? Project,
    IReadOnlyList<string> ActiveStates,
    IReadOnlyList<string> TerminalStates);

public sealed record WorkflowPollingOptions(int IntervalMs);

public sealed record WorkflowWorkspaceOptions(string Root);

public sealed record WorkflowHookOptions(
    string? AfterCreate,
    string? BeforeRun,
    string? AfterRun,
    string? BeforeRemove,
    int TimeoutMs);

public sealed record WorkflowAgentOptions(
    int MaxConcurrentAgents,
    int MaxTurns,
    int MaxRetryBackoffMs,
    IReadOnlyDictionary<string, int> MaxConcurrentAgentsByState);

public sealed record WorkflowCodexOptions(
    string Command,
    string? ApprovalPolicy,
    string? ThreadSandbox,
    IReadOnlyDictionary<string, object?>? TurnSandboxPolicy,
    int TurnTimeoutMs,
    int ReadTimeoutMs,
    int StallTimeoutMs);
