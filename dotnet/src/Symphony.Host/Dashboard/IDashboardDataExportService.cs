namespace Symphony.Host.Dashboard;

public interface IDashboardDataExportService
{
    Task<DashboardDataExportEnvelope?> ExportSingleSessionAsync(string issueIdentifier, CancellationToken cancellationToken = default);

    Task<DashboardDataExportEnvelope> ExportFullBundleAsync(CancellationToken cancellationToken = default);
}
