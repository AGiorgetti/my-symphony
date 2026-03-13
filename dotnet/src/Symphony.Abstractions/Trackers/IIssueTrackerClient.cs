using Symphony.Domain.Issues;

namespace Symphony.Abstractions.Trackers;

public interface IIssueTrackerClient
{
    Task<IReadOnlyList<Issue>> FetchCandidateIssuesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Issue>> FetchIssuesByStatesAsync(
        IReadOnlyCollection<string> stateNames,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Issue>> FetchIssueStatesByIdsAsync(
        IReadOnlyCollection<string> issueIds,
        CancellationToken cancellationToken = default);
}
