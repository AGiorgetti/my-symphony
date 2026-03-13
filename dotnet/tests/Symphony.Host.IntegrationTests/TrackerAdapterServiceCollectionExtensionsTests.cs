using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Symphony.Abstractions.Trackers;
using Symphony.Host.Composition;

namespace Symphony.Host.IntegrationTests;

public class TrackerAdapterServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData(TrackerAdapterKinds.GitHub)]
    [InlineData(TrackerAdapterKinds.AzureDevOps)]
    [InlineData(TrackerAdapterKinds.Linear)]
    public void AddConfiguredTrackerAdapter_registers_selected_tracker_adapter(string trackerKind)
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(trackerKind);

        services.AddConfiguredTrackerAdapter(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        var registration = Assert.Single(serviceProvider.GetServices<TrackerAdapterRegistration>());

        Assert.Equal(trackerKind, registration.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void AddConfiguredTrackerAdapter_rejects_invalid_tracker_kind(string? trackerKind)
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(trackerKind);

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddConfiguredTrackerAdapter(configuration));

        Assert.Contains("tracker:kind", exception.Message);
    }

    private static IConfiguration BuildConfiguration(string? trackerKind)
    {
        var values = new Dictionary<string, string?>();
        if (trackerKind is not null)
        {
            values["tracker:kind"] = trackerKind;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
