using Microsoft.Extensions.Logging;
using Symphony.Abstractions.Processes;
using Symphony.Abstractions.Workflows;
using Symphony.Abstractions.Workspaces;
using Symphony.Application.Configuration;
using Symphony.Application.Orchestration;
using Symphony.Domain.Runs;
using Symphony.Infrastructure.Codex;
using Symphony.Infrastructure.Processes;
using Symphony.Infrastructure.Workflows;

namespace Symphony.Infrastructure.Orchestration;

internal sealed class CodexQueuedIssueWorker(
    IWorkflowOptionsProvider workflowOptionsProvider,
    IWorkflowLoader workflowLoader,
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

        context.UpdateStatus(RunAttemptStatus.PreparingWorkspace);
        var workspace = await workspaceManager.CreateForIssueAsync(context.Issue.Identifier, context.CancellationToken).ConfigureAwait(false);
        var shouldRunAfterRunHook = false;

        try
        {
            if (workflowOptions.Hooks.BeforeRun is not null)
            {
                await RunRequiredHookAsync(
                        "before_run",
                        workflowOptions.Hooks.BeforeRun,
                        context.Issue.Identifier,
                        workspace.Path,
                        workflowOptions.Hooks.TimeoutMs,
                        context.CancellationToken)
                    .ConfigureAwait(false);
            }

            shouldRunAfterRunHook = true;

            context.UpdateStatus(RunAttemptStatus.BuildingPrompt);
            var workflowDefinition = await workflowLoader.LoadAsync(GetWorkflowPath(), context.CancellationToken).ConfigureAwait(false);
            var prompt = workflowPromptRenderer.Render(workflowDefinition, context.Issue, context.Attempt);

            context.UpdateStatus(RunAttemptStatus.LaunchingAgentProcess);
            await codexAppServerClient.RunAsync(context, workspace.Path, prompt, workflowOptions.Codex).ConfigureAwait(false);
            context.UpdateStatus(RunAttemptStatus.Finishing);
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
            if (shouldRunAfterRunHook && workflowOptions.Hooks.AfterRun is not null)
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

    private static string GetWorkflowPath()
    {
        return Path.Combine(Directory.GetCurrentDirectory(), "WORKFLOW.md");
    }
}
