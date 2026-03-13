using Microsoft.Extensions.DependencyInjection;
using Symphony.Abstractions.Trackers;

namespace Symphony.Tracker.AzureDevOps.DependencyInjection;

public static class AzureDevOpsTrackerServiceCollectionExtensions
{
    public static IServiceCollection AddAzureDevOpsTrackerAdapter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new TrackerAdapterRegistration(TrackerAdapterKinds.AzureDevOps));

        return services;
    }
}
