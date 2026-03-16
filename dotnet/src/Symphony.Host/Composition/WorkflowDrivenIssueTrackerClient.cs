using Symphony.Abstractions.Trackers;
using Symphony.Domain.Issues;
using Symphony.Tracker.AzureDevOps;
using Symphony.Tracker.GitHub;
using Symphony.Tracker.Linear;

namespace Symphony.Host.Composition;

public sealed class WorkflowDrivenIssueTrackerClient(
    ITrackerClientOptionsProvider trackerClientOptionsProvider,
    GitHubIssueTrackerClient gitHubIssueTrackerClient,
    AzureDevOpsIssueTrackerClient azureDevOpsIssueTrackerClient,
    LinearIssueTrackerClient linearIssueTrackerClient) : IIssueTrackerClient
{
    public async Task<IReadOnlyList<Issue>> FetchCandidateIssuesAsync(CancellationToken cancellationToken = default)
    {
        var issueTrackerClient = await ResolveCurrentClientAsync(cancellationToken).ConfigureAwait(false);
        return await issueTrackerClient.FetchCandidateIssuesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Issue>> FetchIssuesByStatesAsync(
        IReadOnlyCollection<string> stateNames,
        CancellationToken cancellationToken = default)
    {
        var issueTrackerClient = await ResolveCurrentClientAsync(cancellationToken).ConfigureAwait(false);
        return await issueTrackerClient.FetchIssuesByStatesAsync(stateNames, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Issue>> FetchIssueStatesByIdsAsync(
        IReadOnlyCollection<string> issueIds,
        CancellationToken cancellationToken = default)
    {
        var issueTrackerClient = await ResolveCurrentClientAsync(cancellationToken).ConfigureAwait(false);
        return await issueTrackerClient.FetchIssueStatesByIdsAsync(issueIds, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IIssueTrackerClient> ResolveCurrentClientAsync(CancellationToken cancellationToken)
    {
        var trackerOptions = await trackerClientOptionsProvider.GetCurrentAsync(cancellationToken).ConfigureAwait(false);

        return trackerOptions.Kind switch
        {
            TrackerAdapterKinds.GitHub => gitHubIssueTrackerClient,
            TrackerAdapterKinds.AzureDevOps => azureDevOpsIssueTrackerClient,
            TrackerAdapterKinds.Linear => linearIssueTrackerClient,
            _ => throw new InvalidOperationException(
                $"Tracker kind '{trackerOptions.Kind}' is not supported by the host.")
        };
    }
}
