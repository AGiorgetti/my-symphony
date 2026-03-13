namespace Symphony.Domain.Runs;

public sealed record RunAttempt
{
    public RunAttempt(
        string issueId,
        string issueIdentifier,
        int? attempt,
        string workspacePath,
        DateTimeOffset startedAt,
        RunAttemptStatus status,
        string? error = null)
    {
        IssueId = Guard.Required(issueId, nameof(issueId));
        IssueIdentifier = Guard.Required(issueIdentifier, nameof(issueIdentifier));

        if (attempt is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), attempt, "Attempt must be null or greater than zero.");
        }

        Attempt = attempt;
        WorkspacePath = Guard.Required(workspacePath, nameof(workspacePath));
        StartedAt = startedAt;
        Status = status;
        Error = Guard.Optional(error);
    }

    public string IssueId { get; }

    public string IssueIdentifier { get; }

    public int? Attempt { get; }

    public string WorkspacePath { get; }

    public DateTimeOffset StartedAt { get; }

    public RunAttemptStatus Status { get; }

    public string? Error { get; }
}
