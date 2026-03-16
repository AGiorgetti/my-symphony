using System.Text;
using Microsoft.Extensions.Logging;
using Symphony.Abstractions.Processes;
using Symphony.Abstractions.Workspaces;
using Symphony.Application.Configuration;
using Symphony.Domain.Workspaces;
using Symphony.Infrastructure.Processes;

namespace Symphony.Infrastructure.Workspaces;

public sealed class WorkspaceManager(
    IWorkflowOptionsProvider workflowOptionsProvider,
    IProcessRunner processRunner,
    ILogger<WorkspaceManager> logger) : IWorkspaceManager
{
    public async Task<Workspace> CreateForIssueAsync(string issueIdentifier, CancellationToken cancellationToken = default)
    {
        var options = await workflowOptionsProvider.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        var workspacePathInfo = ResolveWorkspacePath(issueIdentifier, options.Workspace.Root);

        if (File.Exists(workspacePathInfo.WorkspacePath) && !Directory.Exists(workspacePathInfo.WorkspacePath))
        {
            throw new InvalidOperationException(
                $"Cannot create workspace '{workspacePathInfo.WorkspacePath}' because a non-directory entry already exists at that path.");
        }

        var createdNow = false;
        if (!Directory.Exists(workspacePathInfo.WorkspacePath))
        {
            Directory.CreateDirectory(workspacePathInfo.WorkspacePath);
            createdNow = true;
        }

        try
        {
            if (createdNow && options.Hooks.AfterCreate is not null)
            {
                await RunRequiredHookAsync(
                        "after_create",
                        options.Hooks.AfterCreate,
                        issueIdentifier,
                        workspacePathInfo.WorkspacePath,
                        options.Hooks.TimeoutMs,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            if (createdNow && Directory.Exists(workspacePathInfo.WorkspacePath))
            {
                Directory.Delete(workspacePathInfo.WorkspacePath, recursive: true);
            }

            throw;
        }

        logger.LogInformation(
            "workspace_create completed issue_identifier={issue_identifier} workspace_key={workspace_key} workspace_path={workspace_path} created_now={created_now} outcome=completed",
            issueIdentifier,
            workspacePathInfo.WorkspaceKey,
            workspacePathInfo.WorkspacePath,
            createdNow);
        return new Workspace(workspacePathInfo.WorkspacePath, workspacePathInfo.WorkspaceKey, createdNow);
    }

    public async Task DeleteForIssueAsync(string issueIdentifier, CancellationToken cancellationToken = default)
    {
        var options = await workflowOptionsProvider.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        var workspacePathInfo = ResolveWorkspacePath(issueIdentifier, options.Workspace.Root);

        if (Directory.Exists(workspacePathInfo.WorkspacePath))
        {
            if (options.Hooks.BeforeRemove is not null)
            {
                await RunBestEffortHookAsync(
                        "before_remove",
                        options.Hooks.BeforeRemove,
                        issueIdentifier,
                        workspacePathInfo.WorkspacePath,
                        options.Hooks.TimeoutMs,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            Directory.Delete(workspacePathInfo.WorkspacePath, recursive: true);
            logger.LogInformation(
                "workspace_cleanup completed issue_identifier={issue_identifier} workspace_key={workspace_key} workspace_path={workspace_path} outcome=completed",
                issueIdentifier,
                workspacePathInfo.WorkspaceKey,
                workspacePathInfo.WorkspacePath);
            return;
        }

        if (File.Exists(workspacePathInfo.WorkspacePath))
        {
            File.Delete(workspacePathInfo.WorkspacePath);
            logger.LogInformation(
                "workspace_cleanup completed issue_identifier={issue_identifier} workspace_key={workspace_key} workspace_path={workspace_path} outcome=completed",
                issueIdentifier,
                workspacePathInfo.WorkspaceKey,
                workspacePathInfo.WorkspacePath);
        }
    }

    private async Task RunRequiredHookAsync(
        string hookName,
        string script,
        string issueIdentifier,
        string workspacePath,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var result = await RunHookAsync(hookName, script, issueIdentifier, workspacePath, timeoutMs, cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Workspace hook '{hookName}' failed for '{workspacePath}' with exit code {result.ExitCode}.");
        }
    }

    private async Task RunBestEffortHookAsync(
        string hookName,
        string script,
        string issueIdentifier,
        string workspacePath,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await RunHookAsync(hookName, script, issueIdentifier, workspacePath, timeoutMs, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                logger.LogWarning(
                    "workspace_hook failed issue_identifier={issue_identifier} hook_name={hook_name} workspace_path={workspace_path} exit_code={exit_code} outcome=failed",
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
                "workspace_hook failed issue_identifier={issue_identifier} hook_name={hook_name} workspace_path={workspace_path} outcome=failed",
                issueIdentifier,
                hookName,
                workspacePath);
        }
    }

    private async Task<ProcessRunResult> RunHookAsync(
        string hookName,
        string script,
        string issueIdentifier,
        string workspacePath,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "workspace_hook started issue_identifier={issue_identifier} hook_name={hook_name} workspace_path={workspace_path} timeout_ms={timeout_ms} outcome=started",
            issueIdentifier,
            hookName,
            workspacePath,
            timeoutMs);

        return await processRunner.RunAsync(
                CreateHookRequest(script, workspacePath, timeoutMs),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static ProcessRunRequest CreateHookRequest(string script, string workspacePath, int timeoutMs)
    {
        return ShellCommandRequestFactory.Create(script, workspacePath, timeoutMs);
    }

    private static WorkspacePathInfo ResolveWorkspacePath(string issueIdentifier, string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issueIdentifier);

        var workspaceKey = SanitizeIssueIdentifier(issueIdentifier);
        var normalizedWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        var workspacePath = Path.GetFullPath(Path.Combine(normalizedWorkspaceRoot, workspaceKey));

        if (!IsWithinRoot(normalizedWorkspaceRoot, workspacePath))
        {
            throw new InvalidOperationException(
                $"Workspace path '{workspacePath}' must stay inside the configured workspace root '{normalizedWorkspaceRoot}'.");
        }

        return new WorkspacePathInfo(workspacePath, workspaceKey);
    }

    private static string SanitizeIssueIdentifier(string issueIdentifier)
    {
        var sanitized = new StringBuilder(issueIdentifier.Trim().Length);
        var previousWasHyphen = false;

        foreach (var character in issueIdentifier.Trim())
        {
            var normalizedCharacter = IsAllowedWorkspaceCharacter(character) ? character : '-';
            if (normalizedCharacter == '-' && previousWasHyphen)
            {
                continue;
            }

            sanitized.Append(normalizedCharacter);
            previousWasHyphen = normalizedCharacter == '-';
        }

        var workspaceKey = sanitized.ToString().Trim('-');
        if (string.IsNullOrWhiteSpace(workspaceKey))
        {
            throw new InvalidOperationException("Issue identifier did not produce a valid workspace key.");
        }

        return workspaceKey;
    }

    private static bool IsAllowedWorkspaceCharacter(char character)
    {
        return char.IsLetterOrDigit(character) || character is '.' or '_' or '-';
    }

    private static bool IsWithinRoot(string workspaceRoot, string workspacePath)
    {
        var normalizedWorkspaceRoot = Path.TrimEndingDirectorySeparator(workspaceRoot);
        var normalizedWorkspacePath = Path.TrimEndingDirectorySeparator(workspacePath);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return normalizedWorkspacePath.StartsWith(
            normalizedWorkspaceRoot + Path.DirectorySeparatorChar,
            comparison);
    }

    private sealed record WorkspacePathInfo(string WorkspacePath, string WorkspaceKey);
}
