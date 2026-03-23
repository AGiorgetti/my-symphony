using Symphony.Domain.Issues;
using Symphony.Domain.Runs;
using Symphony.Domain.Sessions;

namespace Symphony.Application.Orchestration;

public sealed class QueuedIssueExecutionContext
{
    private readonly Action<Issue> _updateIssue;
    private readonly Action<LiveSessionMetadata> _updateSession;
    private readonly Action<RunAttemptStatus, string?> _updateStatus;

    internal QueuedIssueExecutionContext(
        Issue issue,
        int? attempt,
        CancellationToken cancellationToken,
        Action<Issue> updateIssue,
        Action<LiveSessionMetadata> updateSession,
        Action<RunAttemptStatus, string?> updateStatus)
    {
        Issue = issue ?? throw new ArgumentNullException(nameof(issue));
        Attempt = attempt;
        CancellationToken = cancellationToken;
        _updateIssue = updateIssue ?? throw new ArgumentNullException(nameof(updateIssue));
        _updateSession = updateSession ?? throw new ArgumentNullException(nameof(updateSession));
        _updateStatus = updateStatus ?? throw new ArgumentNullException(nameof(updateStatus));
    }

    public Issue Issue { get; private set; }

    public int? Attempt { get; }

    public CancellationToken CancellationToken { get; }

    public LiveSessionMetadata? Session { get; private set; }

    public string? SessionId => Session?.SessionId;

    public void UpdateIssue(Issue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        Issue = issue;
        _updateIssue(issue);
    }

    public void UpdateSession(LiveSessionMetadata session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Session = session;
        _updateSession(session);
    }

    public void UpdateStatus(RunAttemptStatus status, string? error = null)
    {
        _updateStatus(status, error);
    }
}
