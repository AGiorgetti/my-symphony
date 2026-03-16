using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Symphony.Abstractions.Orchestration;
using Symphony.Application.Configuration;
using Symphony.Domain.Issues;

namespace Symphony.Application.Orchestration;

public sealed class OrchestratorDispatchQueue(
    IWorkflowOptionsProvider workflowOptionsProvider,
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
    private readonly HashSet<string> _claimed = new(StringComparer.Ordinal);

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

        await RefreshConcurrencyGateAsync(cancellationToken).ConfigureAwait(false);

        lock (_stateLock)
        {
            if (_claimed.Contains(issue.Id))
            {
                logger.LogDebug(
                    "Issue {IssueIdentifier} is already claimed and will not be enqueued again.",
                    issue.Identifier);

                return DispatchEnqueueResult.AlreadyClaimed;
            }
        }

        var concurrencyGate = GetConcurrencyGate();
        var acquiredSlot = await concurrencyGate.WaitAsync(0, cancellationToken).ConfigureAwait(false);
        if (!acquiredSlot)
        {
            logger.LogDebug(
                "Issue {IssueIdentifier} could not be enqueued because no execution slots are available.",
                issue.Identifier);

            return DispatchEnqueueResult.NoCapacity;
        }

        var workItem = new DispatchQueueWorkItem(issue, attempt, timeProvider.GetUtcNow());

        lock (_stateLock)
        {
            if (!_claimed.Add(issue.Id))
            {
                ReleaseExecutionSlot();

                logger.LogDebug(
                    "Issue {IssueIdentifier} became claimed before it could be enqueued.",
                    issue.Identifier);

                return DispatchEnqueueResult.AlreadyClaimed;
            }

            _queued[issue.Id] = new QueuedDispatchEntry(issue, attempt, workItem.QueuedAt);
        }

        try
        {
            await _dispatchChannel.Writer.WriteAsync(workItem, cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Enqueued issue {IssueIdentifier} for dispatch attempt {Attempt}.",
                issue.Identifier,
                attempt);

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
                _configuredConcurrency,
                Math.Max(_configuredConcurrency - _queued.Count - _running.Count, 0));
        }
    }

    internal IAsyncEnumerable<DispatchQueueWorkItem> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _dispatchChannel.Reader.ReadAllAsync(cancellationToken);
    }

    internal IDisposable BeginExecution(DispatchQueueWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        var startedAt = timeProvider.GetUtcNow();

        lock (_stateLock)
        {
            _queued.Remove(workItem.Issue.Id);
            _running[workItem.Issue.Id] = new RunningDispatchEntry(workItem.Issue, workItem.Attempt, startedAt);
        }

        logger.LogInformation(
            "Started queued execution for issue {IssueIdentifier} at {StartedAt}.",
            workItem.Issue.Identifier,
            startedAt);

        return new ExecutionLease(this, workItem.Issue);
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
                    return;
                }

                permitsToAdd -= _pendingPermitReduction;
                _pendingPermitReduction = 0;
                _concurrencyGate.Release(permitsToAdd);
                return;
            }

            var permitsToRemove = _configuredConcurrency - targetConcurrency;
            _configuredConcurrency = targetConcurrency;

            while (permitsToRemove > 0 && _concurrencyGate.Wait(0))
            {
                permitsToRemove--;
            }

            _pendingPermitReduction += permitsToRemove;
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

    private void CompleteExecution(Issue issue)
    {
        lock (_stateLock)
        {
            _running.Remove(issue.Id);
            _claimed.Remove(issue.Id);
        }

        ReleaseExecutionSlot();

        logger.LogInformation(
            "Completed queued execution for issue {IssueIdentifier}.",
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

    private sealed class ExecutionLease(OrchestratorDispatchQueue owner, Issue issue) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            owner.CompleteExecution(issue);
        }
    }
}
