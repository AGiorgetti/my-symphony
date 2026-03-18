namespace Symphony.Application.Polling;

public sealed class PollingStatusTracker
{
    private readonly Lock stateLock = new();
    private PollingStatusSnapshot snapshot = new(
        LastStartedAt: null,
        LastCompletedAt: null,
        LastSuccessfulTickAt: null,
        LastFailedAt: null,
        LastError: null);

    public void RecordStarted(DateTimeOffset startedAt)
    {
        lock (stateLock)
        {
            snapshot = snapshot with
            {
                LastStartedAt = startedAt
            };
        }
    }

    public void RecordCompleted(DateTimeOffset completedAt)
    {
        lock (stateLock)
        {
            snapshot = snapshot with
            {
                LastCompletedAt = completedAt,
                LastSuccessfulTickAt = completedAt,
                LastError = null
            };
        }
    }

    public void RecordFailed(DateTimeOffset failedAt, string? error)
    {
        lock (stateLock)
        {
            snapshot = snapshot with
            {
                LastCompletedAt = failedAt,
                LastFailedAt = failedAt,
                LastError = string.IsNullOrWhiteSpace(error) ? null : error.Trim()
            };
        }
    }

    public PollingStatusSnapshot GetSnapshot()
    {
        lock (stateLock)
        {
            return snapshot;
        }
    }
}

public sealed record PollingStatusSnapshot(
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastCompletedAt,
    DateTimeOffset? LastSuccessfulTickAt,
    DateTimeOffset? LastFailedAt,
    string? LastError);
