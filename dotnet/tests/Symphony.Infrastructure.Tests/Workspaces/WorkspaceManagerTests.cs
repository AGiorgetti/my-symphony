using Microsoft.Extensions.Logging.Abstractions;
using Symphony.Abstractions.Processes;
using Symphony.Application.Configuration;
using Symphony.Infrastructure.Workspaces;

namespace Symphony.Infrastructure.Tests.Workspaces;

public sealed class WorkspaceManagerTests
{
    [Fact]
    public async Task CreateForIssueAsync_creates_deterministic_sanitized_workspace()
    {
        var workspaceRoot = CreateTemporaryWorkspaceRoot();

        try
        {
            var manager = CreateWorkspaceManager(
                workspaceRoot,
                processRunner: new RecordingProcessRunner());

            var createdWorkspace = await manager.CreateForIssueAsync("GH-123 / fix bug");
            var reusedWorkspace = await manager.CreateForIssueAsync("GH-123 / fix bug");

            Assert.Equal("GH-123-fix-bug", createdWorkspace.WorkspaceKey);
            Assert.Equal(Path.Combine(Path.GetFullPath(workspaceRoot), "GH-123-fix-bug"), createdWorkspace.Path);
            Assert.True(createdWorkspace.CreatedNow);
            Assert.False(reusedWorkspace.CreatedNow);
            Assert.True(Directory.Exists(createdWorkspace.Path));
            Assert.Equal(createdWorkspace.Path, reusedWorkspace.Path);
        }
        finally
        {
            DeleteDirectoryIfPresent(workspaceRoot);
        }
    }

    [Fact]
    public async Task CreateForIssueAsync_canonicalizes_existing_workspace_root_segments_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var workspaceParent = CreateTemporaryWorkspaceRoot();
        var canonicalWorkspaceRoot = Path.Combine(workspaceParent, "CaseSensitiveRoot");
        Directory.CreateDirectory(canonicalWorkspaceRoot);

        try
        {
            var configuredWorkspaceRoot = Path.Combine(workspaceParent, "casesensitiveroot");
            var manager = CreateWorkspaceManager(
                configuredWorkspaceRoot,
                processRunner: new RecordingProcessRunner());

            var workspace = await manager.CreateForIssueAsync("ISSUE-42");

            Assert.Equal(Path.Combine(canonicalWorkspaceRoot, "ISSUE-42"), workspace.Path);
        }
        finally
        {
            DeleteDirectoryIfPresent(workspaceParent);
        }
    }

    [Fact]
    public async Task CreateForIssueAsync_rejects_workspace_path_outside_root()
    {
        var workspaceRoot = CreateTemporaryWorkspaceRoot();

        try
        {
            var manager = CreateWorkspaceManager(
                workspaceRoot,
                processRunner: new RecordingProcessRunner());

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.CreateForIssueAsync(".."));

            Assert.Contains("must stay inside the configured workspace root", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfPresent(workspaceRoot);
        }
    }

    [Fact]
    public async Task CreateForIssueAsync_runs_after_create_hook_only_for_new_workspace()
    {
        var workspaceRoot = CreateTemporaryWorkspaceRoot();
        var processRunner = new RecordingProcessRunner();

        try
        {
            var manager = CreateWorkspaceManager(
                workspaceRoot,
                processRunner,
                afterCreate: "Write-Host created",
                hookTimeoutMs: 12_345);

            var workspace = await manager.CreateForIssueAsync("ISSUE-42");
            _ = await manager.CreateForIssueAsync("ISSUE-42");

            var request = Assert.Single(processRunner.Requests);
            Assert.Equal(workspace.Path, request.WorkingDirectory);
            Assert.Equal(TimeSpan.FromMilliseconds(12_345), request.Timeout);
            Assert.Equal(OperatingSystem.IsWindows() ? "powershell" : "sh", request.FileName);
            Assert.Equal("Write-Host created", request.Arguments.Last());
        }
        finally
        {
            DeleteDirectoryIfPresent(workspaceRoot);
        }
    }

    [Fact]
    public async Task CreateForIssueAsync_after_create_failure_removes_new_workspace()
    {
        var workspaceRoot = CreateTemporaryWorkspaceRoot();

        try
        {
            var manager = CreateWorkspaceManager(
                workspaceRoot,
                processRunner: new RecordingProcessRunner(resultFactory: _ => new ProcessRunResult(
                    exitCode: 1,
                    standardOutput: string.Empty,
                    standardError: "hook failed",
                    startedAt: DateTimeOffset.UtcNow,
                    finishedAt: DateTimeOffset.UtcNow)),
                afterCreate: "Write-Error boom");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.CreateForIssueAsync("ISSUE-99"));

            Assert.Contains("after_create", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(Path.GetFullPath(workspaceRoot), "ISSUE-99")));
        }
        finally
        {
            DeleteDirectoryIfPresent(workspaceRoot);
        }
    }

    [Fact]
    public async Task CreateForIssueAsync_waits_for_in_progress_after_create_hook_before_reusing_workspace()
    {
        var workspaceRoot = CreateTemporaryWorkspaceRoot();
        var processRunner = new BlockingProcessRunner();

        try
        {
            var manager = CreateWorkspaceManager(
                workspaceRoot,
                processRunner,
                afterCreate: "Write-Host created");

            var firstCreateTask = manager.CreateForIssueAsync("ISSUE-42");
            await processRunner.HookStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var secondCreateTask = manager.CreateForIssueAsync("ISSUE-42");
            await Task.Delay(100);

            Assert.False(secondCreateTask.IsCompleted, "Concurrent callers should wait for the first after_create hook to finish.");

            processRunner.AllowCompletion.SetResult();

            var createdWorkspace = await firstCreateTask;
            var reusedWorkspace = await secondCreateTask;

            Assert.Single(processRunner.Requests);
            Assert.True(createdWorkspace.CreatedNow);
            Assert.False(reusedWorkspace.CreatedNow);
            Assert.Equal(createdWorkspace.Path, reusedWorkspace.Path);
        }
        finally
        {
            DeleteDirectoryIfPresent(workspaceRoot);
        }
    }

    [Fact]
    public async Task DeleteForIssueAsync_runs_before_remove_and_ignores_failures()
    {
        var workspaceRoot = CreateTemporaryWorkspaceRoot();
        var processRunner = new RecordingProcessRunner(exceptionFactory: _ => new InvalidOperationException("hook failed"));

        try
        {
            var manager = CreateWorkspaceManager(
                workspaceRoot,
                processRunner,
                beforeRemove: "Write-Host cleanup");
            var workspacePath = Path.Combine(Path.GetFullPath(workspaceRoot), "ISSUE-123");
            Directory.CreateDirectory(workspacePath);

            await manager.DeleteForIssueAsync("ISSUE-123");

            Assert.Single(processRunner.Requests);
            Assert.False(Directory.Exists(workspacePath));
        }
        finally
        {
            DeleteDirectoryIfPresent(workspaceRoot);
        }
    }

    [Fact]
    public async Task DeleteForIssueAsync_rejects_workspace_root_path()
    {
        var workspaceRoot = CreateTemporaryWorkspaceRoot();

        try
        {
            var manager = CreateWorkspaceManager(
                workspaceRoot,
                processRunner: new RecordingProcessRunner());

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.DeleteForIssueAsync("."));

            Assert.Contains("must stay inside the configured workspace root", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfPresent(workspaceRoot);
        }
    }

    [Fact]
    public async Task DeleteForIssueAsync_rejects_empty_workspace_key()
    {
        var workspaceRoot = CreateTemporaryWorkspaceRoot();

        try
        {
            var manager = CreateWorkspaceManager(
                workspaceRoot,
                processRunner: new RecordingProcessRunner());

            var exception = await Assert.ThrowsAsync<ArgumentException>(() => manager.DeleteForIssueAsync("   "));

            Assert.Contains("empty string or composed entirely of whitespace", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfPresent(workspaceRoot);
        }
    }

    private static WorkspaceManager CreateWorkspaceManager(
        string workspaceRoot,
        IProcessRunner processRunner,
        string? afterCreate = null,
        string? beforeRemove = null,
        int hookTimeoutMs = 60_000)
    {
        return new WorkspaceManager(
            new StaticWorkflowOptionsProvider(CreateWorkflowOptions(workspaceRoot, afterCreate, beforeRemove, hookTimeoutMs)),
            processRunner,
            NullLogger<WorkspaceManager>.Instance);
    }

    private static WorkflowServiceOptions CreateWorkflowOptions(
        string workspaceRoot,
        string? afterCreate,
        string? beforeRemove,
        int hookTimeoutMs)
    {
        return new WorkflowServiceOptions(
            new WorkflowTrackerOptions(
                "github",
                "https://api.github.com",
                "token",
                null,
                "owner/repo",
                null,
                null,
                ["open"],
                ["closed"]),
            new WorkflowPollingOptions(30_000),
            new WorkflowWorkspaceOptions(workspaceRoot),
            new WorkflowHookOptions(afterCreate, null, null, beforeRemove, hookTimeoutMs),
            new WorkflowAgentOptions(1, 20, 300_000, new Dictionary<string, int>(StringComparer.Ordinal), false, "exec:agent"),
            new WorkflowCodexOptions("codex app-server", null, null, null, 3_600_000, 5_000, 300_000));
    }

    private static string CreateTemporaryWorkspaceRoot()
    {
        return Path.Combine(Path.GetTempPath(), "symphony-workspaces-tests", Guid.NewGuid().ToString("N"));
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class StaticWorkflowOptionsProvider(WorkflowServiceOptions workflowOptions) : IWorkflowOptionsProvider
    {
        public Task<WorkflowServiceOptions> GetCurrentAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(workflowOptions);
        }
    }

    private sealed class RecordingProcessRunner(
        Func<ProcessRunRequest, ProcessRunResult>? resultFactory = null,
        Func<ProcessRunRequest, Exception>? exceptionFactory = null) : IProcessRunner
    {
        public List<ProcessRunRequest> Requests { get; } = [];

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            if (exceptionFactory is not null)
            {
                return Task.FromException<ProcessRunResult>(exceptionFactory(request));
            }

            return Task.FromResult(
                resultFactory?.Invoke(request) ?? new ProcessRunResult(
                    exitCode: 0,
                    standardOutput: string.Empty,
                    standardError: string.Empty,
                    startedAt: DateTimeOffset.UtcNow,
                    finishedAt: DateTimeOffset.UtcNow));
        }
    }

    private sealed class BlockingProcessRunner : IProcessRunner
    {
        public List<ProcessRunRequest> Requests { get; } = [];

        public TaskCompletionSource HookStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            HookStarted.TrySetResult();
            await AllowCompletion.Task.WaitAsync(cancellationToken);

            return new ProcessRunResult(
                exitCode: 0,
                standardOutput: string.Empty,
                standardError: string.Empty,
                startedAt: DateTimeOffset.UtcNow,
                finishedAt: DateTimeOffset.UtcNow);
        }
    }
}
