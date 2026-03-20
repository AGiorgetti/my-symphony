using Symphony.Abstractions.Orchestration;
using Symphony.Application.Configuration;
using Symphony.Application.Polling;

namespace Symphony.Host.Health;

public sealed class ServiceHealthSnapshotProvider(
    PollingStatusTracker pollingStatusTracker,
    IOrchestratorControlStatusReader orchestratorControlStatusReader,
    IWorkflowLoadStatusReader workflowLoadStatusReader,
    TimeProvider timeProvider)
{
    private const string Healthy = "Healthy";
    private const string Degraded = "Degraded";
    private const string Paused = "Paused";
    private const string Starting = "Starting";
    private static readonly TimeSpan MinimumStaleThreshold = TimeSpan.FromSeconds(30);

    public ServiceHealthSnapshot GetSnapshot()
    {
        var pollingSnapshot = pollingStatusTracker.GetSnapshot();
        var orchestratorSnapshot = orchestratorControlStatusReader.GetSnapshot();
        var workflowSnapshot = workflowLoadStatusReader.GetSnapshot();
        var now = timeProvider.GetUtcNow();
        var lastPollTickAt = pollingSnapshot.LastCompletedAt ?? pollingSnapshot.LastStartedAt;
        TimeSpan? lastSuccessfulPollAge = pollingSnapshot.LastSuccessfulTickAt is null
            ? null
            : now - pollingSnapshot.LastSuccessfulTickAt.Value;
        var pollStaleThreshold = GetPollStaleThreshold(workflowSnapshot.PollingIntervalMs);
        var pollIsStale = lastSuccessfulPollAge is not null && lastSuccessfulPollAge > pollStaleThreshold;
        var pollFailed = pollingSnapshot.LastFailedAt is not null
            && (pollingSnapshot.LastSuccessfulTickAt is null || pollingSnapshot.LastFailedAt > pollingSnapshot.LastSuccessfulTickAt);
        var workflowFailed = string.Equals(
            workflowSnapshot.Status,
            "ReloadFailedUsingLastKnownGood",
            StringComparison.Ordinal);

        var status = orchestratorSnapshot.State == OrchestratorControlState.Stopped
            ? Paused
            : workflowFailed || pollFailed || pollIsStale
            ? Degraded
            : pollingSnapshot.LastSuccessfulTickAt is not null
                ? Healthy
                : Starting;

        return new ServiceHealthSnapshot(
            status,
            orchestratorSnapshot.State,
            orchestratorSnapshot.ChangedAt,
            lastPollTickAt,
            pollingSnapshot.LastSuccessfulTickAt,
            lastSuccessfulPollAge?.TotalSeconds,
            pollIsStale,
            workflowSnapshot.Status,
            workflowSnapshot.LastSuccessfulLoadAt,
            workflowSnapshot.WorkflowPath,
            pollingSnapshot.LastError,
            workflowSnapshot.LastError);
    }

    private static TimeSpan GetPollStaleThreshold(int? pollingIntervalMs)
    {
        if (pollingIntervalMs is null || pollingIntervalMs <= 0)
        {
            return MinimumStaleThreshold;
        }

        return TimeSpan.FromMilliseconds(Math.Max(pollingIntervalMs.Value * 2L, (long)MinimumStaleThreshold.TotalMilliseconds));
    }
}

public sealed record ServiceHealthSnapshot(
    string Status,
    OrchestratorControlState OrchestratorState,
    DateTimeOffset OrchestratorStateChangedAt,
    DateTimeOffset? LastPollTickAt,
    DateTimeOffset? LastSuccessfulPollAt,
    double? LastSuccessfulPollAgeSeconds,
    bool PollIsStale,
    string WorkflowLoadStatus,
    DateTimeOffset? WorkflowLastLoadedAt,
    string? WorkflowPath,
    string? PollLastError,
    string? WorkflowLastError);
