using Symphony.Application.Configuration;

namespace Symphony.Infrastructure.Configuration;

public sealed class WorkflowLoadStatusTracker : IWorkflowLoadStatusReader
{
    private readonly Lock _stateLock = new();
    private WorkflowLoadStatusSnapshot _snapshot = new(
        Status: "Starting",
        WorkflowPath: null,
        LastSuccessfulLoadAt: null,
        LastFailedAt: null,
        LastErrorCode: null,
        LastError: null,
        PollingIntervalMs: null);

    public void RecordLoaded(string workflowPath, WorkflowServiceOptions workflowOptions, DateTimeOffset loadedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowPath);
        ArgumentNullException.ThrowIfNull(workflowOptions);

        lock (_stateLock)
        {
            _snapshot = _snapshot with
            {
                Status = "Loaded",
                WorkflowPath = workflowPath,
                LastSuccessfulLoadAt = loadedAt,
                LastErrorCode = null,
                LastError = null,
                PollingIntervalMs = workflowOptions.Polling.IntervalMs
            };
        }
    }

    public void RecordFailed(string workflowPath, string errorCode, string error, DateTimeOffset failedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        lock (_stateLock)
        {
            _snapshot = _snapshot with
            {
                Status = "ReloadFailedUsingLastKnownGood",
                WorkflowPath = workflowPath,
                LastFailedAt = failedAt,
                LastErrorCode = errorCode,
                LastError = error.Trim()
            };
        }
    }

    public WorkflowLoadStatusSnapshot GetSnapshot()
    {
        lock (_stateLock)
        {
            return _snapshot;
        }
    }
}
