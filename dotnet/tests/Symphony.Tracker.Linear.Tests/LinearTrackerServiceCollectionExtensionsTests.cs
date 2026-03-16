using Microsoft.Extensions.DependencyInjection;
using Symphony.Abstractions.Trackers;
using Symphony.Tracker.Linear.DependencyInjection;

namespace Symphony.Tracker.Linear.Tests;

public sealed class LinearTrackerServiceCollectionExtensionsTests
{
    [Fact]
    public void AddLinearTrackerAdapter_registers_tracker_services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITrackerClientOptionsProvider>(new StubTrackerClientOptionsProvider());

        services.AddLinearTrackerAdapter();

        using var serviceProvider = services.BuildServiceProvider();

        Assert.IsType<LinearIssueTrackerClient>(serviceProvider.GetRequiredService<LinearIssueTrackerClient>());
        Assert.Equal(TrackerAdapterKinds.Linear, serviceProvider.GetRequiredService<TrackerAdapterRegistration>().Kind);
    }

    private sealed class StubTrackerClientOptionsProvider : ITrackerClientOptionsProvider
    {
        public Task<TrackerClientOptions> GetCurrentAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new TrackerClientOptions(
                    TrackerAdapterKinds.Linear,
                    "https://api.linear.app/graphql",
                    "token",
                    null,
                    "symphony",
                    null,
                    null,
                    ["Todo"],
                    ["Done"]));
        }
    }
}
