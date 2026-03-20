using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Symphony.Abstractions.Orchestration;
using Symphony.Application.Polling;

namespace Symphony.Application.Orchestration;

public sealed class OrchestratorControlService : IOrchestratorControl, IOrchestratorControlStatusReader, IOrchestratorExecutionGate
{
    private readonly Lock _stateLock = new();
    private readonly PollingRefreshTrigger _pollingRefreshTrigger;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrchestratorControlService> _logger;
    private TaskCompletionSource _resumeSignal;
    private OrchestratorControlSnapshot _snapshot;

    public OrchestratorControlService(
        IOptions<OrchestratorControlOptions> options,
        PollingRefreshTrigger pollingRefreshTrigger,
        TimeProvider timeProvider,
        ILogger<OrchestratorControlService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _pollingRefreshTrigger = pollingRefreshTrigger;
        _timeProvider = timeProvider;
        _logger = logger;

        var initialState = ParseState(options.Value.InitialState);
        _snapshot = new OrchestratorControlSnapshot(initialState, _timeProvider.GetUtcNow());
        _resumeSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

        if (initialState == OrchestratorControlState.Started)
        {
            _resumeSignal.TrySetResult();
        }

        _logger.LogInformation(
            "orchestrator_control initialized state={state} changed_at={changed_at:O} outcome=configured",
            initialState,
            _snapshot.ChangedAt);
    }

    public OrchestratorControlSnapshot GetSnapshot()
    {
        lock (_stateLock)
        {
            return _snapshot;
        }
    }

    public Task RequestRefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _pollingRefreshTrigger.RequestRefresh();
        return Task.CompletedTask;
    }

    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var changedAt = _timeProvider.GetUtcNow();
        var transitioned = false;

        lock (_stateLock)
        {
            if (_snapshot.State == OrchestratorControlState.Stopped)
            {
                return Task.CompletedTask;
            }

            _snapshot = new OrchestratorControlSnapshot(OrchestratorControlState.Stopped, changedAt);
            _resumeSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
            transitioned = true;
        }

        if (transitioned)
        {
            _pollingRefreshTrigger.RequestRefresh();
            _logger.LogInformation(
                "orchestrator_control paused state={state} changed_at={changed_at:O} outcome=paused",
                OrchestratorControlState.Stopped,
                changedAt);
        }

        return Task.CompletedTask;
    }

    public Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var changedAt = _timeProvider.GetUtcNow();
        TaskCompletionSource? resumeSignal = null;

        lock (_stateLock)
        {
            if (_snapshot.State == OrchestratorControlState.Started)
            {
                return Task.CompletedTask;
            }

            _snapshot = new OrchestratorControlSnapshot(OrchestratorControlState.Started, changedAt);
            resumeSignal = _resumeSignal;
        }

        resumeSignal.TrySetResult();
        _pollingRefreshTrigger.RequestRefresh();
        _logger.LogInformation(
            "orchestrator_control resumed state={state} changed_at={changed_at:O} outcome=resumed",
            OrchestratorControlState.Started,
            changedAt);

        return Task.CompletedTask;
    }

    public Task WaitUntilStartedAsync(CancellationToken cancellationToken = default)
    {
        Task waitTask;

        lock (_stateLock)
        {
            if (_snapshot.State == OrchestratorControlState.Started)
            {
                return Task.CompletedTask;
            }

            waitTask = _resumeSignal.Task;
        }

        return waitTask.WaitAsync(cancellationToken);
    }

    private static OrchestratorControlState ParseState(string? initialState)
    {
        if (Enum.TryParse<OrchestratorControlState>(initialState, ignoreCase: true, out var state))
        {
            return state;
        }

        throw new InvalidOperationException(
            $"Invalid orchestration initial state '{initialState}'. Use 'Started' or 'Stopped'.");
    }
}
