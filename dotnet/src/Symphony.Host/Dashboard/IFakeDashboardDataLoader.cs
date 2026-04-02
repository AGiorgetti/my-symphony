namespace Symphony.Host.Dashboard;

public interface IFakeDashboardDataLoader
{
    (FakeDashboardDataSet DataSet, FakeDashboardDataStatus Status) LoadConfigured(FakeDashboardDataSet builtInDataSet);

    Task<(FakeDashboardDataSet DataSet, FakeDashboardDataStatus Status)> LoadFromStreamAsync(
        Stream jsonStream,
        string? sourceName,
        FakeDashboardDataSet builtInDataSet,
        CancellationToken cancellationToken = default);
}
