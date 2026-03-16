using Microsoft.Extensions.DependencyInjection;
using Symphony.Abstractions.Trackers;
using Symphony.Tracker.AzureDevOps.DependencyInjection;

namespace Symphony.Tracker.AzureDevOps.Tests;

public sealed class AzureDevOpsTrackerServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAzureDevOpsTrackerAdapter_registers_tracker_services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITrackerClientOptionsProvider>(new StubTrackerClientOptionsProvider());

        services.AddAzureDevOpsTrackerAdapter();

        using var serviceProvider = services.BuildServiceProvider();

        Assert.IsType<AzureDevOpsIssueTrackerClient>(serviceProvider.GetRequiredService<AzureDevOpsIssueTrackerClient>());
        Assert.Equal(TrackerAdapterKinds.AzureDevOps, serviceProvider.GetRequiredService<TrackerAdapterRegistration>().Kind);
    }

    private sealed class StubTrackerClientOptionsProvider : ITrackerClientOptionsProvider
    {
        public Task<TrackerClientOptions> GetCurrentAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new TrackerClientOptions(
                    TrackerAdapterKinds.AzureDevOps,
                    "https://dev.azure.com",
                    "token",
                    null,
                    null,
                    "AGiorgetti",
                    "my-symphony",
                    ["Active"],
                    ["Closed"]));
        }
    }
}
