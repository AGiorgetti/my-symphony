using Symphony.Abstractions.Orchestration;

namespace Symphony.Application.Orchestration;

public sealed record FollowUpActionResolutionRequest(
    string IssueIdentifier,
    string FollowUpActionId,
    string ResolvedBy,
    string? SelectedOptionId,
    string? Notes);

public sealed record FollowUpActionResolutionResult(
    FollowUpActionResolutionStatus Status,
    bool Requeued,
    FollowUpActionSnapshot? Action,
    string? Message);

public enum FollowUpActionResolutionStatus
{
    Resolved,
    ActionNotFound,
    BlockedIssueNotFound,
    IssueNotDispatchable
}
