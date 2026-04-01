namespace Symphony.Application.Runtime;

public sealed class AttemptHistoryTracker
{
    private const int MaxEntries = 20;

    private readonly Lock _stateLock = new();
    private readonly LinkedList<RecentAttemptSnapshot> _attempts = [];

    public void Record(
        string issueId,
        string issueIdentifier,
        int? attempt,
        string outcome,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string? error = null,
        string? sessionId = null,
        string? orchestratorSessionId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issueId);
        ArgumentException.ThrowIfNullOrWhiteSpace(issueIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome);

        var durationSeconds = Math.Max((completedAt - startedAt).TotalSeconds, 0d);
        var snapshot = new RecentAttemptSnapshot(
            issueId.Trim(),
            issueIdentifier.Trim(),
            attempt,
            outcome.Trim(),
            startedAt,
            completedAt,
            durationSeconds,
            string.IsNullOrWhiteSpace(error) ? null : error.Trim(),
            string.IsNullOrWhiteSpace(sessionId) ? null : sessionId.Trim(),
            string.IsNullOrWhiteSpace(orchestratorSessionId) ? null : orchestratorSessionId.Trim());

        lock (_stateLock)
        {
            _attempts.AddFirst(snapshot);

            while (_attempts.Count > MaxEntries)
            {
                _attempts.RemoveLast();
            }
        }
    }

    public IReadOnlyList<RecentAttemptSnapshot> GetRecentAttempts()
    {
        lock (_stateLock)
        {
            return _attempts.ToArray();
        }
    }
}

public sealed record RecentAttemptSnapshot(
    string IssueId,
    string IssueIdentifier,
    int? Attempt,
    string Outcome,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    double DurationSeconds,
    string? Error,
    string? SessionId,
    string? OrchestratorSessionId = null);
