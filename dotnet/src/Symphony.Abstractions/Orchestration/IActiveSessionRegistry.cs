namespace Symphony.Abstractions.Orchestration;

public interface IActiveSessionRegistry
{
    IReadOnlyList<ActiveSessionSnapshot> GetActiveSessions();

    bool TryCancelForReconciliation(string issueId, string? trackerState = null);
}
