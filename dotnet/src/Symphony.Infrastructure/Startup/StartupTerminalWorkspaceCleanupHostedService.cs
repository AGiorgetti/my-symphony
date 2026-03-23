using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Symphony.Abstractions.Trackers;
using Symphony.Abstractions.Workspaces;
using Symphony.Application.Configuration;
using Symphony.Domain.Issues;

namespace Symphony.Infrastructure.Startup;

public sealed class StartupTerminalWorkspaceCleanupHostedService(
    IWorkflowOptionsProvider workflowOptionsProvider,
    IIssueTrackerClient issueTrackerClient,
    IWorkspaceManager workspaceManager,
    ILogger<StartupTerminalWorkspaceCleanupHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var workflowOptions = await workflowOptionsProvider.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        var terminalStates = workflowOptions.Tracker.TerminalStates
            .Where(state => !string.IsNullOrWhiteSpace(state))
            .Select(state => state.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        logger.LogInformation(
            "startup_terminal_cleanup started terminal_state_count={terminal_state_count} outcome=started",
            terminalStates.Length);

        if (terminalStates.Length == 0)
        {
            logger.LogInformation(
                "startup_terminal_cleanup completed terminal_issue_count={terminal_issue_count} cleaned_count={cleaned_count} failed_count={failed_count} outcome=completed",
                0,
                0,
                0);
            return;
        }

        IReadOnlyList<Issue> terminalIssues;
        try
        {
            terminalIssues = await issueTrackerClient.FetchIssuesByStatesAsync(terminalStates, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "startup_terminal_cleanup failed terminal_state_count={terminal_state_count} reason=terminal_issue_fetch_failed outcome=continued",
                terminalStates.Length);
            return;
        }

        var cleanedCount = 0;
        var failedCount = 0;
        var cleanedIdentifiers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var issue in terminalIssues)
        {
            if (!cleanedIdentifiers.Add(issue.Identifier))
            {
                continue;
            }

            try
            {
                await workspaceManager.DeleteForIssueAsync(issue.Identifier, cancellationToken).ConfigureAwait(false);
                cleanedCount++;

                logger.LogInformation(
                    "startup_terminal_cleanup cleaned issue_id={issue_id} issue_identifier={issue_identifier} outcome=cleaned",
                    issue.Id,
                    issue.Identifier);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failedCount++;
                logger.LogWarning(
                    exception,
                    "startup_terminal_cleanup failed issue_id={issue_id} issue_identifier={issue_identifier} reason=workspace_cleanup_failed outcome=continued",
                    issue.Id,
                    issue.Identifier);
            }
        }

        logger.LogInformation(
            "startup_terminal_cleanup completed terminal_issue_count={terminal_issue_count} cleaned_count={cleaned_count} failed_count={failed_count} outcome=completed",
            cleanedIdentifiers.Count,
            cleanedCount,
            failedCount);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
