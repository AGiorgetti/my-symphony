using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Symphony.Abstractions.Orchestration;
using Symphony.Application.Configuration;
using Symphony.Domain.Issues;

namespace Symphony.Application.Orchestration;

public sealed class OrchestratorDispatchQueue(
    IWorkflowOptionsProvider workflowOptionsProvider,
    RetryDelayPlanner retryDelayPlanner,
    TimeProvider timeProvider,
    ILogger<OrchestratorDispatchQueue> logger) : IOrchestratorDispatchQueue, IOrchestratorDispatchStatusReader
{
    private const int DispatchQueueCapacity = 4_096;

    private readonly Channel<DispatchQueueWorkItem> _dispatchChannel =
        Channel.CreateBounded<DispatchQueueWorkItem>(
            new BoundedChannelOptions(DispatchQueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

    private readonly Lock _stateLock = new();
    private readonly Dictionary<string, QueuedDispatchEntry> _queued = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RunningDispatchEntry> _running = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RetryDispatchEntry> _retrying = new(StringComparer.Ordinal);
    private readonly HashSet<string> _claimed = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _retrySignal = new(0, int.MaxValue);

    private SemaphoreSlim? _concurrencyGate;
    private int _configuredConcurrency;
    private int _pendingPermitReduction;

    public async ValueTask<DispatchEnqueueResult> QueueAsync(
        Issue issue,
        int? attempt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(issue);

        if (attempt is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), attempt, "Attempt must be null or greater than zero.");
        }

        return await QueueInternalAsync(issue, attempt, claimRequired: true, cancellationToken).ConfigureAwait(false);
    }

    public DispatchQueueSnapshot GetSnapshot()
    {
        lock (_stateLock)
        {
            return new DispatchQueueSnapshot(
                _queued.Values
                    .OrderBy(entry => entry.QueuedAt)
                    .Select(
                        entry => new QueuedDispatchSnapshot(
                            entry.Issue.Id,
                            entry.Issue.Identifier,
                            entry.Issue.State,
                            entry.Attempt,
                            entry.QueuedAt))
                    .ToArray(),
                _running.Values
                    .OrderBy(entry => entry.StartedAt)
                    .Select(
                        entry => new RunningDispatchSnapshot(
                            entry.Issue.Id,
                            entry.Issue.Identifier,
                            entry.Issue.State,
                            entry.Attempt,
                            entry.StartedAt))
                    .ToArray(),
                _retrying.Values
                    .OrderBy(entry => entry.DueAt)
                    .Select(
                        entry => new RetryDispatchSnapshot(
                            entry.Issue.Id,
                            entry.Issue.Identifier,
                            entry.Attempt,
                            entry.DueAt,
                            entry.Error))
                    .ToArray(),
                _configuredConcurrency,
                Math.Max(_configuredConcurrency - _queued.Count - _running.Count, 0));
        }
    }

    internal IAsyncEnumerable<DispatchQueueWorkItem> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _dispatchChannel.Reader.ReadAllAsync(cancellationToken);
    }

    internal ExecutionLease BeginExecution(DispatchQueueWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        var startedAt = timeProvider.GetUtcNow();

        lock (_stateLock)
        {
            _queued.Remove(workItem.Issue.Id);
            _running[workItem.Issue.Id] = new RunningDispatchEntry(workItem.Issue, workItem.Attempt, startedAt);
        }

        logger.LogInformation(
            "dispatch_execution started issue_id={issue_id} issue_identifier={issue_identifier} attempt={attempt} started_at={started_at:O} outcome=started",
            workItem.Issue.Id,
            workItem.Issue.Identifier,
            workItem.Attempt,
            startedAt);

        return new ExecutionLease(this, workItem.Issue);
    }

    internal async Task ScheduleContinuationRetryAsync(
        DispatchQueueWorkItem workItem,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        var delay = retryDelayPlanner.GetContinuationDelay();
        await ScheduleRetryInternalAsync(
                workItem.Issue,
                attempt: 1,
                dueAt: timeProvider.GetUtcNow().Add(delay),
                error: null,
                logReason: "continuation",
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal async Task ScheduleFailureRetryAsync(
        DispatchQueueWorkItem workItem,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentNullException.ThrowIfNull(exception);

        var nextAttempt = workItem.Attempt is null
            ? 1
            : workItem.Attempt.Value + 1;
        var workflowOptions = await workflowOptionsProvider.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        var delay = await retryDelayPlanner.GetFailureDelayAsync(
                nextAttempt,
                workflowOptions.Agent.MaxRetryBackoffMs,
                cancellationToken)
            .ConfigureAwait(false);

        await ScheduleRetryInternalAsync(
                workItem.Issue,
                nextAttempt,
                timeProvider.GetUtcNow().Add(delay),
                exception.Message,
                "failure",
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal void ReleaseClaim(string issueId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issueId);

        lock (_stateLock)
        {
            _retrying.Remove(issueId);
            _claimed.Remove(issueId);
        }
    }

    internal TimeSpan? GetTimeUntilNextRetry()
    {
        lock (_stateLock)
        {
            if (_retrying.Count == 0)
            {
                return null;
            }

            var now = timeProvider.GetUtcNow();
            var nextDueAt = _retrying.Values.Min(entry => entry.DueAt);
            return nextDueAt <= now ? TimeSpan.Zero : nextDueAt - now;
        }
    }

    internal Task WaitForRetrySignalAsync(CancellationToken cancellationToken)
    {
        return _retrySignal.WaitAsync(cancellationToken);
    }

    internal bool TryRefreshRunningIssue(Issue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        lock (_stateLock)
        {
            if (!_running.TryGetValue(issue.Id, out var entry))
            {
                return false;
            }

            _running[issue.Id] = entry with { Issue = issue };
        }

        logger.LogInformation(
            "dispatch_execution refreshed issue_id={issue_id} issue_identifier={issue_identifier} issue_state={issue_state} outcome=completed",
            issue.Id,
            issue.Identifier,
            issue.State);
        return true;
    }

    internal async Task ProcessDueRetriesAsync(CancellationToken cancellationToken)
    {
        RetryDispatchEntry[] dueEntries;
        lock (_stateLock)
        {
            var now = timeProvider.GetUtcNow();
            dueEntries = _retrying.Values
                .Where(entry => entry.DueAt <= now)
                .OrderBy(entry => entry.DueAt)
                .ToArray();

            foreach (var dueEntry in dueEntries)
            {
                _retrying.Remove(dueEntry.Issue.Id);
            }
        }

        foreach (var dueEntry in dueEntries)
        {
            var enqueueResult = await QueueInternalAsync(
                    dueEntry.Issue,
                    dueEntry.Attempt,
                    claimRequired: false,
                    cancellationToken)
                .ConfigureAwait(false);

            if (enqueueResult == DispatchEnqueueResult.Enqueued)
            {
                logger.LogInformation(
                    "dispatch_retry dispatched issue_id={issue_id} issue_identifier={issue_identifier} attempt={attempt} due_at={due_at:O} outcome=completed",
                    dueEntry.Issue.Id,
                    dueEntry.Issue.Identifier,
                    dueEntry.Attempt,
                    dueEntry.DueAt);
                continue;
            }

            if (enqueueResult == DispatchEnqueueResult.NoCapacity)
            {
                var workflowOptions = await workflowOptionsProvider.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
                var nextAttempt = dueEntry.Attempt + 1;
                var delay = await retryDelayPlanner.GetFailureDelayAsync(
                        nextAttempt,
                        workflowOptions.Agent.MaxRetryBackoffMs,
                        cancellationToken)
                    .ConfigureAwait(false);

                await ScheduleRetryInternalAsync(
                        dueEntry.Issue,
                        nextAttempt,
                        timeProvider.GetUtcNow().Add(delay),
                        "no available orchestrator slots",
                        "capacity",
                        cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            ReleaseClaim(dueEntry.Issue.Id);
        }
    }

    private async ValueTask<DispatchEnqueueResult> QueueInternalAsync(
        Issue issue,
        int? attempt,
        bool claimRequired,
        CancellationToken cancellationToken)
    {
        await RefreshConcurrencyGateAsync(cancellationToken).ConfigureAwait(false);

        lock (_stateLock)
        {
            if (claimRequired)
            {
                if (_claimed.Contains(issue.Id))
                {
                    logger.LogDebug(
                        "dispatch_enqueue skipped issue_id={issue_id} issue_identifier={issue_identifier} attempt={attempt} reason=already_claimed outcome=skipped",
                        issue.Id,
                        issue.Identifier,
                        attempt);

                    return DispatchEnqueueResult.AlreadyClaimed;
                }
            }
            else if (_queued.ContainsKey(issue.Id) || _running.ContainsKey(issue.Id) || _retrying.ContainsKey(issue.Id))
            {
                return DispatchEnqueueResult.AlreadyClaimed;
            }
        }

        var concurrencyGate = GetConcurrencyGate();
        var acquiredSlot = await concurrencyGate.WaitAsync(0, cancellationToken).ConfigureAwait(false);
        if (!acquiredSlot)
        {
            logger.LogDebug(
                "dispatch_enqueue skipped issue_id={issue_id} issue_identifier={issue_identifier} attempt={attempt} reason=no_capacity outcome=skipped",
                issue.Id,
                issue.Identifier,
                attempt);

            return DispatchEnqueueResult.NoCapacity;
        }

        var workItem = new DispatchQueueWorkItem(issue, attempt, timeProvider.GetUtcNow());

        lock (_stateLock)
        {
            if (claimRequired && !_claimed.Add(issue.Id))
            {
                ReleaseExecutionSlot();

                logger.LogDebug(
                    "dispatch_enqueue skipped issue_id={issue_id} issue_identifier={issue_identifier} attempt={attempt} reason=claimed_before_queue outcome=skipped",
                    issue.Id,
                    issue.Identifier,
                    attempt);

                return DispatchEnqueueResult.AlreadyClaimed;
            }

            _queued[issue.Id] = new QueuedDispatchEntry(issue, attempt, workItem.QueuedAt);
        }

        try
        {
            await _dispatchChannel.Writer.WriteAsync(workItem, cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "dispatch_enqueue completed issue_id={issue_id} issue_identifier={issue_identifier} attempt={attempt} queued_at={queued_at:O} outcome=enqueued",
                issue.Id,
                issue.Identifier,
                attempt,
                workItem.QueuedAt);

            return DispatchEnqueueResult.Enqueued;
        }
        catch
        {
            lock (_stateLock)
            {
                _queued.Remove(issue.Id);
                _claimed.Remove(issue.Id);
            }

            ReleaseExecutionSlot();
            throw;
        }
    }

    private Task ScheduleRetryInternalAsync(
        Issue issue,
        int attempt,
        DateTimeOffset dueAt,
        string? error,
        string logReason,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_stateLock)
        {
            _retrying[issue.Id] = new RetryDispatchEntry(issue, attempt, dueAt, string.IsNullOrWhiteSpace(error) ? null : error.Trim());
        }

        if (_retrySignal.CurrentCount == 0)
        {
            _retrySignal.Release();
        }

        logger.LogInformation(
            "dispatch_retry scheduled issue_id={issue_id} issue_identifier={issue_identifier} attempt={attempt} due_at={due_at:O} reason={reason} error={error} outcome=scheduled",
            issue.Id,
            issue.Identifier,
            attempt,
            dueAt,
            logReason,
            error);

        return Task.CompletedTask;
    }

    private async Task RefreshConcurrencyGateAsync(CancellationToken cancellationToken)
    {
        var workflowOptions = await workflowOptionsProvider.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        var targetConcurrency = workflowOptions.Agent.MaxConcurrentAgents;

        lock (_stateLock)
        {
            if (_concurrencyGate is null)
            {
                _concurrencyGate = new SemaphoreSlim(targetConcurrency, int.MaxValue);
                _configuredConcurrency = targetConcurrency;
                _pendingPermitReduction = 0;
                logger.LogInformation(
                    "dispatch_capacity completed max_concurrent_agents={max_concurrent_agents} outcome=configured",
                    targetConcurrency);
                return;
            }

            if (targetConcurrency == _configuredConcurrency)
            {
                return;
            }

            if (targetConcurrency > _configuredConcurrency)
            {
                var permitsToAdd = targetConcurrency - _configuredConcurrency;
                _configuredConcurrency = targetConcurrency;

                if (_pendingPermitReduction >= permitsToAdd)
                {
                    _pendingPermitReduction -= permitsToAdd;
                    logger.LogInformation(
                        "dispatch_capacity completed max_concurrent_agents={max_concurrent_agents} outcome=updated",
                        targetConcurrency);
                    return;
                }

                permitsToAdd -= _pendingPermitReduction;
                _pendingPermitReduction = 0;
                _concurrencyGate.Release(permitsToAdd);
                logger.LogInformation(
                    "dispatch_capacity completed max_concurrent_agents={max_concurrent_agents} outcome=updated",
                    targetConcurrency);
                return;
            }

            var permitsToRemove = _configuredConcurrency - targetConcurrency;
            _configuredConcurrency = targetConcurrency;

            while (permitsToRemove > 0 && _concurrencyGate.Wait(0))
            {
                permitsToRemove--;
            }

            _pendingPermitReduction += permitsToRemove;
            logger.LogInformation(
                "dispatch_capacity completed max_concurrent_agents={max_concurrent_agents} outcome=updated",
                targetConcurrency);
        }
    }

    private SemaphoreSlim GetConcurrencyGate()
    {
        lock (_stateLock)
        {
            return _concurrencyGate
                ?? throw new InvalidOperationException("The dispatch queue concurrency gate has not been initialized.");
        }
    }

    private void CompleteExecution(Issue issue, bool releaseClaim)
    {
        lock (_stateLock)
        {
            _running.Remove(issue.Id);
            if (releaseClaim)
            {
                _claimed.Remove(issue.Id);
            }
        }

        ReleaseExecutionSlot();

        logger.LogInformation(
            "dispatch_execution completed issue_id={issue_id} issue_identifier={issue_identifier} outcome=completed",
            issue.Id,
            issue.Identifier);
    }

    private void ReleaseExecutionSlot()
    {
        lock (_stateLock)
        {
            if (_concurrencyGate is null)
            {
                return;
            }

            if (_pendingPermitReduction > 0)
            {
                _pendingPermitReduction--;
                return;
            }

            _concurrencyGate.Release();
        }
    }

    internal sealed record DispatchQueueWorkItem(Issue Issue, int? Attempt, DateTimeOffset QueuedAt);

    private sealed record QueuedDispatchEntry(Issue Issue, int? Attempt, DateTimeOffset QueuedAt);

    private sealed record RunningDispatchEntry(Issue Issue, int? Attempt, DateTimeOffset StartedAt);

    private sealed record RetryDispatchEntry(Issue Issue, int Attempt, DateTimeOffset DueAt, string? Error);

    internal sealed class ExecutionLease(OrchestratorDispatchQueue owner, Issue issue) : IDisposable
    {
        private int _disposed;
        private bool _releaseClaim = true;

        public void PreserveClaimForRetry()
        {
            _releaseClaim = false;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            owner.CompleteExecution(issue, _releaseClaim);
        }
    }
}
