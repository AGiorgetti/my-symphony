namespace Symphony.Application.Configuration;

public interface IWorkflowLoadStatusReader
{
    WorkflowLoadStatusSnapshot GetSnapshot();
}

public sealed record WorkflowLoadStatusSnapshot(
    string Status,
    string? WorkflowPath,
    DateTimeOffset? LastSuccessfulLoadAt,
    DateTimeOffset? LastFailedAt,
    string? LastErrorCode,
    string? LastError,
    int? PollingIntervalMs);
