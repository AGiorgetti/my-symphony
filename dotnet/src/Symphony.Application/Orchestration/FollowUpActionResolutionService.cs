using Symphony.Abstractions.Orchestration;
using Symphony.Abstractions.Trackers;
using Symphony.Application.Configuration;
using Symphony.Domain.Issues;

namespace Symphony.Application.Orchestration;

public sealed class FollowUpActionResolutionService(
    FollowUpActionRegistry followUpActionRegistry,
    OrchestratorDispatchQueue dispatchQueue,
    IIssueTrackerClient issueTrackerClient,
    IWorkflowOptionsProvider workflowOptionsProvider)
{
    public async Task<FollowUpActionResolutionResult> ResolveAsync(
        FollowUpActionResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var action = followUpActionRegistry.Resolve(
            request.FollowUpActionId,
            request.ResolvedBy,
            request.SelectedOptionId,
            request.Notes);
        if (action is null)
        {
            return new FollowUpActionResolutionResult(
                FollowUpActionResolutionStatus.ActionNotFound,
                Requeued: false,
                Action: null,
                "Follow-up action was not found or is no longer pending.");
        }

        var blockedIssue = dispatchQueue.GetBlockedIssue(request.IssueIdentifier);
        if (blockedIssue is null || !string.Equals(blockedIssue.FollowUpActionId, action.FollowUpActionId, StringComparison.Ordinal))
        {
            dispatchQueue.ReleaseBlockedClaim(action.IssueId);
            return new FollowUpActionResolutionResult(
                FollowUpActionResolutionStatus.BlockedIssueNotFound,
                Requeued: false,
                Action: action,
                "Blocked issue state was not found.");
        }

        var currentIssue = await RefreshIssueAsync(blockedIssue.Issue, cancellationToken).ConfigureAwait(false);
        var workflowOptions = await workflowOptionsProvider.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (!IssueDispatchEligibility.CanDispatch(currentIssue, workflowOptions, out var skipReason))
        {
            dispatchQueue.ReleaseBlockedClaim(currentIssue.Id);
            return new FollowUpActionResolutionResult(
                FollowUpActionResolutionStatus.IssueNotDispatchable,
                Requeued: false,
                Action: action,
                $"Issue is not dispatchable after resolution: {skipReason}.");
        }

        await dispatchQueue.ResumeBlockedAsync(currentIssue, blockedIssue.Attempt, cancellationToken).ConfigureAwait(false);
        return new FollowUpActionResolutionResult(
            FollowUpActionResolutionStatus.Resolved,
            Requeued: true,
            Action: action,
            null);
    }

    private async Task<Issue> RefreshIssueAsync(Issue issue, CancellationToken cancellationToken)
    {
        var refreshedIssues = await issueTrackerClient.FetchIssueStatesByIdsAsync([issue.Id], cancellationToken).ConfigureAwait(false);
        return refreshedIssues.FirstOrDefault(candidate => string.Equals(candidate.Id, issue.Id, StringComparison.Ordinal))
            ?? issue;
    }
}
