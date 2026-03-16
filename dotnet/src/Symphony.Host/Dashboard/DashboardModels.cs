namespace Symphony.Host.Dashboard;

public sealed record DashboardSnapshot(
    string ServiceHealth,
    string OrchestratorMode,
    DateTimeOffset? LastPollTickAt,
    int RunningCount,
    int RetryingCount,
    long InputTokens,
    long OutputTokens,
    long TotalTokens,
    double SecondsRunning,
    string? LastError);
