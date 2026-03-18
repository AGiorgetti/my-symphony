namespace Symphony.Host.Dashboard;

public interface IDashboardStateService
{
    Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
