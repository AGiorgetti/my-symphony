using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Symphony.Abstractions.Trackers;
using Symphony.Tracker.AzureDevOps.DependencyInjection;
using Symphony.Tracker.GitHub.DependencyInjection;
using Symphony.Tracker.Linear.DependencyInjection;

namespace Symphony.Host.Composition;

public static class TrackerAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddConfiguredTrackerAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddGitHubTrackerAdapter();
        services.AddAzureDevOpsTrackerAdapter();
        services.AddLinearTrackerAdapter();
        services.AddSingleton<IIssueTrackerClient, WorkflowDrivenIssueTrackerClient>();

        return services;
    }
}
