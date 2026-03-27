using Symphony.Application.Configuration;
using Symphony.Domain.Issues;

namespace Symphony.Application.Orchestration;

internal static class IssueDispatchEligibility
{
    public static bool CanDispatch(Issue issue, WorkflowServiceOptions workflowOptions, out string skipReason)
    {
        ArgumentNullException.ThrowIfNull(issue);
        ArgumentNullException.ThrowIfNull(workflowOptions);

        return CanDispatch(
            issue,
            CreateStateSet(workflowOptions.Tracker.ActiveStates),
            CreateStateSet(workflowOptions.Tracker.TerminalStates),
            CreateStateSet(workflowOptions.Tracker.DispatchBlockLabels),
            workflowOptions.Agent.RequireExecMarker,
            workflowOptions.Agent.ExecMarker,
            out skipReason);
    }

    public static bool CanDispatch(
        Issue issue,
        HashSet<string> activeStates,
        HashSet<string> terminalStates,
        HashSet<string> dispatchBlockLabels,
        bool requireExecMarker,
        string execMarker,
        out string skipReason)
    {
        ArgumentNullException.ThrowIfNull(issue);
        ArgumentNullException.ThrowIfNull(activeStates);
        ArgumentNullException.ThrowIfNull(terminalStates);
        ArgumentNullException.ThrowIfNull(dispatchBlockLabels);

        if (!activeStates.Contains(issue.NormalizedState) || terminalStates.Contains(issue.NormalizedState))
        {
            skipReason = "state_not_dispatchable";
            return false;
        }

        if (string.Equals(issue.NormalizedState, "todo", StringComparison.Ordinal)
            && issue.BlockedBy.Any(blocker => blocker.NormalizedState is null || !terminalStates.Contains(blocker.NormalizedState)))
        {
            skipReason = "blocked_by_dependency";
            return false;
        }

        if (dispatchBlockLabels.Count > 0
            && issue.Labels.Any(dispatchBlockLabels.Contains))
        {
            skipReason = "blocked_by_label";
            return false;
        }

        if (requireExecMarker && !issue.Labels.Contains(execMarker.Trim().ToLowerInvariant(), StringComparer.Ordinal))
        {
            skipReason = "missing_exec_marker";
            return false;
        }

        skipReason = string.Empty;
        return true;
    }

    public static HashSet<string> CreateStateSet(IEnumerable<string> states)
    {
        ArgumentNullException.ThrowIfNull(states);

        return states
            .Where(state => !string.IsNullOrWhiteSpace(state))
            .Select(state => state.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
    }
}
