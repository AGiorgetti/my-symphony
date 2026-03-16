using Microsoft.Extensions.Logging;
using Symphony.Abstractions.Orchestration;
using Symphony.Domain.Issues;
using Symphony.Domain.Runs;
using Symphony.Domain.Sessions;

namespace Symphony.Application.Orchestration;

public sealed class ActiveSessionRegistry(
    TimeProvider timeProvider,
    ILogger<ActiveSessionRegistry> logger) : IActiveSessionRegistry
{
    private readonly Lock _stateLock = new();
    private readonly Dictionary<string, ActiveSessionEntry> _activeSessions = new(StringComparer.Ordinal);

    public IReadOnlyList<ActiveSessionSnapshot> GetActiveSessions()
    {
        lock (_stateLock)
        {
            return _activeSessions.Values
                .OrderBy(entry => entry.StartedAt)
                .Select(CreateSnapshot)
                .ToArray();
        }
    }

    public bool TryCancelForReconciliation(string issueId, string? trackerState = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issueId);

        var entry = TryMarkCanceledForReconciliation(issueId, trackerState);
        return entry is not null;
    }

    public async Task<bool> TryCancelAndWaitForReconciliationAsync(
        string issueId,
        string? trackerState = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issueId);

        var entry = TryMarkCanceledForReconciliation(issueId, trackerState);
        if (entry is null)
        {
            return false;
        }

        await entry.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public bool TryRefreshIssue(Issue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        lock (_stateLock)
        {
            if (!_activeSessions.TryGetValue(NormalizeIssueId(issue.Id), out var entry))
            {
                return false;
            }

            entry.Issue = issue;
        }

        logger.LogInformation(
            "session_tracking refreshed issue_id={issue_id} issue_identifier={issue_identifier} session_id={session_id} issue_state={issue_state} outcome=completed",
            issue.Id,
            issue.Identifier,
            (string?)null,
            issue.State);
        return true;
    }

    public TrackedActiveSession BeginSession(Issue issue, int? attempt, CancellationToken hostCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issue);

        var normalizedIssueId = NormalizeIssueId(issue.Id);
        var startedAt = timeProvider.GetUtcNow();
        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(hostCancellationToken);
        var entry = new ActiveSessionEntry(issue, attempt, startedAt, cancellationTokenSource)
        {
            Status = RunAttemptStatus.InitializingSession
        };

        lock (_stateLock)
        {
            if (!_activeSessions.TryAdd(normalizedIssueId, entry))
            {
                cancellationTokenSource.Dispose();
                throw new InvalidOperationException(
                    $"An active session for issue '{issue.Id}' is already registered.");
            }
        }

        logger.LogInformation(
            "session_tracking started issue_id={issue_id} issue_identifier={issue_identifier} session_id={session_id} attempt={attempt} outcome=started",
            issue.Id,
            issue.Identifier,
            (string?)null,
            attempt);

        return new TrackedActiveSession(this, normalizedIssueId, entry);
    }

    private void UpdateSession(ActiveSessionEntry entry, LiveSessionMetadata session)
    {
        lock (_stateLock)
        {
            entry.Session = session;
        }

        logger.LogInformation(
            "session_tracking updated issue_id={issue_id} issue_identifier={issue_identifier} session_id={session_id} outcome=completed",
            entry.Issue.Id,
            entry.Issue.Identifier,
            session.SessionId);
    }

    private void UpdateStatus(ActiveSessionEntry entry, RunAttemptStatus status, string? error)
    {
        lock (_stateLock)
        {
            entry.Status = status;
            entry.Error = string.IsNullOrWhiteSpace(error) ? null : error.Trim();
        }

        logger.LogInformation(
            "session_status completed issue_id={issue_id} issue_identifier={issue_identifier} session_id={session_id} status={status} error={error} outcome=completed",
            entry.Issue.Id,
            entry.Issue.Identifier,
            entry.Session?.SessionId,
            status,
            entry.Error ?? string.Empty);
    }

    private bool WasCanceledByReconciliation(ActiveSessionEntry entry)
    {
        lock (_stateLock)
        {
            return entry.CanceledByReconciliation;
        }
    }

    private void Complete(string normalizedIssueId, ActiveSessionEntry entry)
    {
        lock (_stateLock)
        {
            _activeSessions.Remove(normalizedIssueId);
        }

        entry.Completion.TrySetResult();

        logger.LogInformation(
            "session_tracking completed issue_id={issue_id} issue_identifier={issue_identifier} session_id={session_id} status={status} outcome=completed",
            entry.Issue.Id,
            entry.Issue.Identifier,
            entry.Session?.SessionId,
            entry.Status);

        entry.CancellationTokenSource.Dispose();
    }

    private ActiveSessionEntry? TryMarkCanceledForReconciliation(string issueId, string? trackerState)
    {
        ActiveSessionEntry? entry;
        lock (_stateLock)
        {
            _activeSessions.TryGetValue(NormalizeIssueId(issueId), out entry);

            if (entry is null)
            {
                return null;
            }

            entry.Status = RunAttemptStatus.CanceledByReconciliation;
            entry.Error = trackerState is null
                ? "Session canceled after reconciliation determined the issue is no longer eligible."
                : $"Session canceled after reconciliation transitioned the issue to '{trackerState}'.";
            entry.CanceledByReconciliation = true;
        }

        logger.LogInformation(
            "session_cancellation completed issue_id={issue_id} issue_identifier={issue_identifier} session_id={session_id} tracker_state={tracker_state} outcome=canceled",
            entry.Issue.Id,
            entry.Issue.Identifier,
            entry.Session?.SessionId,
            trackerState ?? "unknown");

        entry.CancellationTokenSource.Cancel();
        return entry;
    }

    private static ActiveSessionSnapshot CreateSnapshot(ActiveSessionEntry entry)
    {
        return new ActiveSessionSnapshot(
            entry.Issue.Id,
            entry.Issue.Identifier,
            entry.Issue.State,
            entry.Attempt,
            entry.StartedAt,
            entry.Status,
            entry.Error,
            entry.Session);
    }

    private static string NormalizeIssueId(string issueId)
    {
        return issueId.Trim().ToUpperInvariant();
    }

    public sealed class TrackedActiveSession : IDisposable
    {
        private readonly ActiveSessionRegistry _owner;
        private readonly string _normalizedIssueId;
        private readonly ActiveSessionEntry _entry;
        private int _disposed;

        internal TrackedActiveSession(
            ActiveSessionRegistry owner,
            string normalizedIssueId,
            ActiveSessionEntry entry)
        {
            _owner = owner;
            _normalizedIssueId = normalizedIssueId;
            _entry = entry;
        }

        public CancellationToken CancellationToken => _entry.CancellationTokenSource.Token;

        public bool WasCanceledByReconciliation => _owner.WasCanceledByReconciliation(_entry);

        public QueuedIssueExecutionContext CreateExecutionContext()
        {
            return new QueuedIssueExecutionContext(
                _entry.Issue,
                _entry.Attempt,
                CancellationToken,
                session => _owner.UpdateSession(_entry, session),
                (status, error) => _owner.UpdateStatus(_entry, status, error));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _owner.Complete(_normalizedIssueId, _entry);
        }
    }

    internal sealed class ActiveSessionEntry(
        Issue issue,
        int? attempt,
        DateTimeOffset startedAt,
        CancellationTokenSource cancellationTokenSource)
    {
        public Issue Issue { get; set; } = issue;

        public int? Attempt { get; } = attempt;

        public DateTimeOffset StartedAt { get; } = startedAt;

        public CancellationTokenSource CancellationTokenSource { get; } = cancellationTokenSource;

        public RunAttemptStatus Status { get; set; }

        public string? Error { get; set; }

        public LiveSessionMetadata? Session { get; set; }

        public bool CanceledByReconciliation { get; set; }

        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
