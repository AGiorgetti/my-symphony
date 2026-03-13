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

        if (!TrackerAdapterKinds.TryNormalize(configuration["tracker:kind"], out var trackerKind))
        {
            throw new InvalidOperationException(
                $"Configuration value 'tracker:kind' must be one of: {string.Join(", ", TrackerAdapterKinds.All)}.");
        }

        return trackerKind switch
        {
            TrackerAdapterKinds.GitHub => services.AddGitHubTrackerAdapter(),
            TrackerAdapterKinds.AzureDevOps => services.AddAzureDevOpsTrackerAdapter(),
            TrackerAdapterKinds.Linear => services.AddLinearTrackerAdapter(),
            _ => throw new InvalidOperationException($"Unsupported tracker:kind '{trackerKind}'.")
        };
    }
}
