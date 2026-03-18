using Microsoft.Extensions.Logging;
using Symphony.Abstractions.Processes;
using Symphony.Abstractions.Workspaces;
using Symphony.Application.Configuration;
using Symphony.Application.Orchestration;
using Symphony.Domain.Runs;
using Symphony.Domain.Workspaces;
using Symphony.Infrastructure.Codex;
using Symphony.Infrastructure.Processes;
using Symphony.Infrastructure.Workflows;

namespace Symphony.Infrastructure.Orchestration;

internal sealed class CodexQueuedIssueWorker(
    IWorkflowOptionsProvider workflowOptionsProvider,
    IWorkflowDefinitionProvider workflowDefinitionProvider,
    IWorkspaceManager workspaceManager,
    IProcessRunner processRunner,
    WorkflowPromptRenderer workflowPromptRenderer,
    CodexAppServerClient codexAppServerClient,
    ILogger<CodexQueuedIssueWorker> logger) : IQueuedIssueWorker
{
    public async Task ExecuteAsync(QueuedIssueExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var workflowOptions = await workflowOptionsProvider.GetCurrentAsync(context.CancellationToken).ConfigureAwait(false);
        Workspace? workspace = null;

        context.UpdateStatus(RunAttemptStatus.PreparingWorkspace);
        var shouldRunAfterRunHook = false;

        try
        {
            workspace = await workspaceManager.CreateForIssueAsync(context.Issue.Identifier, context.CancellationToken).ConfigureAwait(false);
            var issueWorkspace = workspace ?? throw new InvalidOperationException("Workspace manager returned null.");

            if (workflowOptions.Hooks.BeforeRun is not null)
            {
                await RunRequiredHookAsync(
                        "before_run",
                        workflowOptions.Hooks.BeforeRun,
                        context.Issue.Identifier,
                        issueWorkspace.Path,
                        workflowOptions.Hooks.TimeoutMs,
                        context.CancellationToken)
                    .ConfigureAwait(false);
            }

            shouldRunAfterRunHook = true;

            context.UpdateStatus(RunAttemptStatus.BuildingPrompt);
            var workflowDefinition = await workflowDefinitionProvider.GetCurrentDefinitionAsync(context.CancellationToken).ConfigureAwait(false);
            var prompt = workflowPromptRenderer.Render(workflowDefinition, context.Issue, context.Attempt);

            context.UpdateStatus(RunAttemptStatus.LaunchingAgentProcess);
            await codexAppServerClient.RunAsync(context, issueWorkspace.Path, prompt, workflowOptions.Codex).ConfigureAwait(false);
            context.UpdateStatus(RunAttemptStatus.Finishing);
        }
        catch (ProcessRunTimedOutException exception)
        {
            throw new IssueExecutionTimedOutException(
                $"Issue '{context.Issue.Identifier}' timed out while running a workflow hook.",
                exception);
        }
        catch (CodexAgentException exception) when (IsTimeout(exception))
        {
            throw new IssueExecutionTimedOutException(
                $"Issue '{context.Issue.Identifier}' timed out while waiting for Codex.",
                exception);
        }
        catch (WorkflowPromptRenderer.WorkflowPromptException exception)
        {
            logger.LogError(
                exception,
                "prompt_render failed issue_id={issue_id} issue_identifier={issue_identifier} outcome=failed",
                context.Issue.Id,
                context.Issue.Identifier);
            throw;
        }
        finally
        {
            if (workspace is not null && shouldRunAfterRunHook && workflowOptions.Hooks.AfterRun is not null)
            {
                await RunBestEffortHookAsync(
                        "after_run",
                        workflowOptions.Hooks.AfterRun,
                        context.Issue.Identifier,
                        workspace.Path,
                        workflowOptions.Hooks.TimeoutMs,
                        context.CancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task RunRequiredHookAsync(
        string hookName,
        string command,
        string issueIdentifier,
        string workspacePath,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var result = await RunHookAsync(hookName, command, issueIdentifier, workspacePath, timeoutMs, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Workflow hook '{hookName}' failed for '{workspacePath}' with exit code {result.ExitCode}.");
        }
    }

    private async Task RunBestEffortHookAsync(
        string hookName,
        string command,
        string issueIdentifier,
        string workspacePath,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await RunHookAsync(hookName, command, issueIdentifier, workspacePath, timeoutMs, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                logger.LogWarning(
                    "workflow_hook failed issue_identifier={issue_identifier} hook_name={hook_name} workspace_path={workspace_path} exit_code={exit_code} outcome=failed",
                    issueIdentifier,
                    hookName,
                    workspacePath,
                    result.ExitCode);
            }
        }
        catch (ProcessRunTimedOutException exception)
        {
            logger.LogWarning(
                exception,
                "workflow_hook timed_out issue_identifier={issue_identifier} hook_name={hook_name} workspace_path={workspace_path} outcome=timed_out",
                issueIdentifier,
                hookName,
                workspacePath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "workflow_hook failed issue_identifier={issue_identifier} hook_name={hook_name} workspace_path={workspace_path} outcome=failed",
                issueIdentifier,
                hookName,
                workspacePath);
        }
    }

    private async Task<ProcessRunResult> RunHookAsync(
        string hookName,
        string command,
        string issueIdentifier,
        string workspacePath,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "workflow_hook started issue_identifier={issue_identifier} hook_name={hook_name} workspace_path={workspace_path} timeout_ms={timeout_ms} outcome=started",
            issueIdentifier,
            hookName,
            workspacePath,
                timeoutMs);

        return await processRunner.RunAsync(
                ShellCommandRequestFactory.Create(command, workspacePath, timeoutMs),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool IsTimeout(CodexAgentException exception)
    {
        return exception.Code is "response_timeout" or "turn_timeout";
    }
}
