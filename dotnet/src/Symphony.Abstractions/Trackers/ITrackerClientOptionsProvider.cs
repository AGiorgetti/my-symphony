namespace Symphony.Abstractions.Trackers;

public interface ITrackerClientOptionsProvider
{
    Task<TrackerClientOptions> GetCurrentAsync(CancellationToken cancellationToken = default);
}
