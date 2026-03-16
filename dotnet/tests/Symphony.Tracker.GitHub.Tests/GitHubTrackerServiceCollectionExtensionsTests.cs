using Microsoft.Extensions.DependencyInjection;
using Symphony.Abstractions.Trackers;
using Symphony.Tracker.GitHub.DependencyInjection;

namespace Symphony.Tracker.GitHub.Tests;

public sealed class GitHubTrackerServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGitHubTrackerAdapter_registers_tracker_services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITrackerClientOptionsProvider>(new StubTrackerClientOptionsProvider());

        services.AddGitHubTrackerAdapter();

        using var serviceProvider = services.BuildServiceProvider();

        Assert.IsType<GitHubIssueTrackerClient>(serviceProvider.GetRequiredService<IIssueTrackerClient>());
        Assert.Equal(TrackerAdapterKinds.GitHub, serviceProvider.GetRequiredService<TrackerAdapterRegistration>().Kind);
    }

    private sealed class StubTrackerClientOptionsProvider : ITrackerClientOptionsProvider
    {
        public Task<TrackerClientOptions> GetCurrentAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new TrackerClientOptions(
                    TrackerAdapterKinds.GitHub,
                    "https://api.github.com",
                    "token",
                    "AGiorgetti/my-symphony",
                    null,
                    null,
                    null,
                    ["open"],
                    ["closed"]));
        }
    }
}
