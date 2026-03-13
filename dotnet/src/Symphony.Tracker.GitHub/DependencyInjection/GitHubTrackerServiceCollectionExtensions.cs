using Microsoft.Extensions.DependencyInjection;
using Symphony.Abstractions.Trackers;

namespace Symphony.Tracker.GitHub.DependencyInjection;

public static class GitHubTrackerServiceCollectionExtensions
{
    public static IServiceCollection AddGitHubTrackerAdapter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new TrackerAdapterRegistration(TrackerAdapterKinds.GitHub));

        return services;
    }
}
