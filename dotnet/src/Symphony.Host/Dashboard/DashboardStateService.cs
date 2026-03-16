using Symphony.Application.Polling;
using Symphony.Application.Runtime;

namespace Symphony.Host.Dashboard;

public sealed class DashboardStateService(
    IOrchestratorRuntimeService orchestratorRuntimeService,
    PollingStatusTracker pollingStatusTracker) : IDashboardStateService
{
    private const string InMemoryMode = "Single-process in-memory";

    public async Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var runtimeSnapshot = await orchestratorRuntimeService.GetStateSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var pollingSnapshot = pollingStatusTracker.GetSnapshot();

        return new DashboardSnapshot(
            DetermineServiceHealth(pollingSnapshot),
            InMemoryMode,
            pollingSnapshot.LastSuccessfulTickAt ?? pollingSnapshot.LastCompletedAt ?? pollingSnapshot.LastStartedAt,
            runtimeSnapshot.Running.Count,
            runtimeSnapshot.Retrying.Count,
            runtimeSnapshot.CodexTotals.InputTokens,
            runtimeSnapshot.CodexTotals.OutputTokens,
            runtimeSnapshot.CodexTotals.TotalTokens,
            runtimeSnapshot.CodexTotals.SecondsRunning,
            pollingSnapshot.LastError);
    }

    private static string DetermineServiceHealth(PollingStatusSnapshot pollingSnapshot)
    {
        if (pollingSnapshot.LastFailedAt is not null
            && (pollingSnapshot.LastSuccessfulTickAt is null || pollingSnapshot.LastFailedAt > pollingSnapshot.LastSuccessfulTickAt))
        {
            return "Degraded";
        }

        if (pollingSnapshot.LastSuccessfulTickAt is not null)
        {
            return "Healthy";
        }

        return "Starting";
    }
}
