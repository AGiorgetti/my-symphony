using Symphony.Application.Polling;

namespace Symphony.Application.Runtime;

public interface IOrchestratorRuntimeService
{
    Task<OrchestratorStateSnapshot> GetStateSnapshotAsync(CancellationToken cancellationToken = default);

    Task<OrchestratorIssueSnapshot?> GetIssueSnapshotAsync(
        string issueIdentifier,
        CancellationToken cancellationToken = default);

    PollingRefreshReceipt RequestRefresh();
}
