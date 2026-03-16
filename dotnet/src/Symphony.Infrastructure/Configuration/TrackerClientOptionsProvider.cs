using Symphony.Abstractions.Trackers;
using Symphony.Application.Configuration;

namespace Symphony.Infrastructure.Configuration;

public sealed class TrackerClientOptionsProvider(IWorkflowOptionsProvider workflowOptionsProvider) : ITrackerClientOptionsProvider
{
    public async Task<TrackerClientOptions> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var workflowOptions = await workflowOptionsProvider.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        var tracker = workflowOptions.Tracker;

        return new TrackerClientOptions(
            tracker.Kind,
            tracker.Endpoint,
            tracker.ApiKey,
            tracker.Repository,
            tracker.ProjectSlug,
            tracker.Organization,
            tracker.Project,
            tracker.ActiveStates,
            tracker.TerminalStates);
    }
}
