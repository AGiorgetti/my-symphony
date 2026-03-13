using Microsoft.Extensions.DependencyInjection;
using Symphony.Abstractions.Trackers;

namespace Symphony.Tracker.Linear.DependencyInjection;

public static class LinearTrackerServiceCollectionExtensions
{
    public static IServiceCollection AddLinearTrackerAdapter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new TrackerAdapterRegistration(TrackerAdapterKinds.Linear));

        return services;
    }
}
