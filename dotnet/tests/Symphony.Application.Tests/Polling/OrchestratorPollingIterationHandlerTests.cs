using Microsoft.Extensions.Logging.Abstractions;
using Symphony.Abstractions.Trackers;
using Symphony.Abstractions.Workspaces;
using Symphony.Application.Configuration;
using Symphony.Application.Orchestration;
using Symphony.Application.Polling;
using Symphony.Application.Tests.Logging;
using Symphony.Domain.Issues;
using Symphony.Domain.Sessions;
using Symphony.Domain.Workspaces;

namespace Symphony.Application.Tests.Polling;

public sealed class OrchestratorPollingIterationHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_dispatches_eligible_candidates_in_spec_priority_order()
    {
        var workflowOptions = CreateWorkflowOptions(
            maxConcurrentAgents: 2,
            maxConcurrentAgentsByState: new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["todo"] = 1
            });
        var trackerClient = new StubIssueTrackerClient
        {
            CandidateIssues =
            [
                CreateIssue("4", "ABC-4", state: "In Progress", priority: null, createdAt: new DateTimeOffset(2026, 3, 16, 12, 4, 0, TimeSpan.Zero)),
                CreateIssue("1", "ABC-1", state: "Todo", priority: 1, createdAt: new DateTimeOffset(2026, 3, 16, 12, 1, 0, TimeSpan.Zero), blockedBy:
                [
                    new IssueBlocker("parent-1", "ABC-0", "Todo")
                ]),
                CreateIssue("2", "ABC-2", state: "Todo", priority: 1, createdAt: new DateTimeOffset(2026, 3, 16, 12, 0, 0, TimeSpan.Zero)),
                CreateIssue("5", "ABC-5", state: "In Progress", priority: 2, createdAt: new DateTimeOffset(2026, 3, 16, 12, 2, 0, TimeSpan.Zero)),
                CreateIssue("3", "ABC-3", state: "Todo", priority: 2, createdAt: new DateTimeOffset(2026, 3, 16, 12, 3, 0, TimeSpan.Zero))
            ]
        };
        var queue = CreateQueue(workflowOptions);
        var registry = CreateRegistry();
        var workspaceManager = new StubWorkspaceManager();
        var handler = CreateHandler(trackerClient, queue, registry, workspaceManager);

        await handler.ExecuteAsync(workflowOptions, CancellationToken.None);

        var queuedIssues = queue.GetSnapshot().Queued.Select(snapshot => snapshot.IssueIdentifier).ToArray();

        Assert.Equal(["ABC-2", "ABC-5"], queuedIssues);
        Assert.Equal(1, trackerClient.FetchCandidateIssuesCalls);
        Assert.Empty(workspaceManager.DeletedIssueIdentifiers);
    }

    [Fact]
    public async Task ExecuteAsync_cancels_terminal_sessions_and_cleans_workspaces()
    {
        var workflowOptions = CreateWorkflowOptions();
        var trackerClient = new StubIssueTrackerClient
        {
            RefreshedIssues =
            [
                CreateIssue("1", "ABC-1", state: "Done")
            ]
        };
        var queue = CreateQueue(workflowOptions);
        var registry = CreateRegistry();
        var workspaceManager = new StubWorkspaceManager();
        var handler = CreateHandler(trackerClient, queue, registry, workspaceManager);
        var trackedSession = registry.BeginSession(CreateIssue("1", "ABC-1", state: "In Progress"), attempt: null, CancellationToken.None);
        var sessionTask = RunUntilCanceledAsync(trackedSession);

        await handler.ExecuteAsync(workflowOptions, CancellationToken.None);
        await sessionTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Empty(registry.GetActiveSessions());
        Assert.Equal(["ABC-1"], workspaceManager.DeletedIssueIdentifiers);
        Assert.Equal(["1"], trackerClient.LastRefreshedIssueIds);
    }

    [Fact]
    public async Task ExecuteAsync_refreshes_active_session_state_when_tracker_issue_remains_active()
    {
        var workflowOptions = CreateWorkflowOptions();
        var trackerClient = new StubIssueTrackerClient
        {
            RefreshedIssues =
            [
                CreateIssue("1", "ABC-1", state: "In Progress")
            ]
        };
        var queue = CreateQueue(workflowOptions);
        var registry = CreateRegistry();
        var handler = CreateHandler(trackerClient, queue, registry, new StubWorkspaceManager());
        using var trackedSession = registry.BeginSession(CreateIssue("1", "ABC-1", state: "Todo"), attempt: null, CancellationToken.None);

        await handler.ExecuteAsync(workflowOptions, CancellationToken.None);

        var snapshot = Assert.Single(registry.GetActiveSessions());
        Assert.Equal("In Progress", snapshot.IssueState);
    }

    [Fact]
    public async Task ExecuteAsync_cancels_non_candidate_sessions_without_workspace_cleanup()
    {
        var workflowOptions = CreateWorkflowOptions();
        var trackerClient = new StubIssueTrackerClient
        {
            RefreshedIssues =
            [
                CreateIssue("1", "ABC-1", state: "Backlog")
            ]
        };
        var queue = CreateQueue(workflowOptions);
        var registry = CreateRegistry();
        var workspaceManager = new StubWorkspaceManager();
        var handler = CreateHandler(trackerClient, queue, registry, workspaceManager);
        var trackedSession = registry.BeginSession(CreateIssue("1", "ABC-1", state: "In Progress"), attempt: null, CancellationToken.None);
        var sessionTask = RunUntilCanceledAsync(trackedSession);

        await handler.ExecuteAsync(workflowOptions, CancellationToken.None);
        await sessionTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Empty(registry.GetActiveSessions());
        Assert.Empty(workspaceManager.DeletedIssueIdentifiers);
    }

    [Fact]
    public async Task ExecuteAsync_marks_stalled_sessions_before_tracker_refresh()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 3, 18, 12, 0, 0, TimeSpan.Zero));
        var workflowOptions = CreateWorkflowOptions(stallTimeoutMs: 300_000);
        var trackerClient = new StubIssueTrackerClient();
        var queue = CreateQueue(workflowOptions, timeProvider);
        var registry = CreateRegistry(timeProvider);
        var workspaceManager = new StubWorkspaceManager();
        var logger = new TestLogger<OrchestratorPollingIterationHandler>();
        var handler = CreateHandler(trackerClient, queue, registry, workspaceManager, timeProvider, logger);
        var trackedSession = registry.BeginSession(CreateIssue("1", "ABC-1", state: "In Progress"), attempt: null, CancellationToken.None);
        var context = trackedSession.CreateExecutionContext();
        context.UpdateSession(
            new LiveSessionMetadata(
                "thread-1",
                "turn-1",
                lastCodexTimestamp: timeProvider.GetUtcNow().AddMinutes(-6),
                lastCodexMessage: "still running",
                turnCount: 1));
        var sessionTask = RunUntilCanceledAsync(trackedSession);

        await handler.ExecuteAsync(workflowOptions, CancellationToken.None);
        await sessionTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Empty(registry.GetActiveSessions());
        Assert.Empty(trackerClient.LastRefreshedIssueIds);

        var stallEntry = Assert.Single(
            logger.Entries,
            entry => entry.Message.Contains("poll_reconcile stalled", StringComparison.Ordinal));
        Assert.Equal("1", Assert.IsType<string>(stallEntry.State["issue_id"]));
        Assert.Equal("ABC-1", Assert.IsType<string>(stallEntry.State["issue_identifier"]));
        Assert.Equal(300000d, Convert.ToDouble(stallEntry.State["stall_timeout_ms"]));
        Assert.Equal(360000d, Convert.ToDouble(stallEntry.State["elapsed_ms"]));
    }

    [Fact]
    public async Task ExecuteAsync_uses_started_at_when_no_codex_event_has_been_seen()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 3, 18, 11, 54, 0, TimeSpan.Zero));
        var workflowOptions = CreateWorkflowOptions(stallTimeoutMs: 300_000);
        var trackerClient = new StubIssueTrackerClient();
        var queue = CreateQueue(workflowOptions, timeProvider);
        var registry = CreateRegistry(timeProvider);
        var handler = CreateHandler(trackerClient, queue, registry, new StubWorkspaceManager(), timeProvider);
        var trackedSession = registry.BeginSession(CreateIssue("1", "ABC-1", state: "In Progress"), attempt: null, CancellationToken.None);
        var sessionTask = RunUntilCanceledAsync(trackedSession);

        timeProvider.SetUtcNow(new DateTimeOffset(2026, 3, 18, 12, 0, 0, TimeSpan.Zero));

        await handler.ExecuteAsync(workflowOptions, CancellationToken.None);
        await sessionTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Empty(registry.GetActiveSessions());
        Assert.Empty(trackerClient.LastRefreshedIssueIds);
    }

    [Fact]
    public async Task ExecuteAsync_skips_stall_detection_when_timeout_is_disabled()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 3, 18, 12, 0, 0, TimeSpan.Zero));
        var workflowOptions = CreateWorkflowOptions(stallTimeoutMs: 0);
        var trackerClient = new StubIssueTrackerClient
        {
            RefreshedIssues =
            [
                CreateIssue("1", "ABC-1", state: "In Progress")
            ]
        };
        var queue = CreateQueue(workflowOptions, timeProvider);
        var registry = CreateRegistry(timeProvider);
        var trackedSession = registry.BeginSession(CreateIssue("1", "ABC-1", state: "In Progress"), attempt: null, CancellationToken.None);
        var context = trackedSession.CreateExecutionContext();
        context.UpdateSession(
            new LiveSessionMetadata(
                "thread-1",
                "turn-1",
                lastCodexTimestamp: timeProvider.GetUtcNow().AddMinutes(-10),
                lastCodexMessage: "still running",
                turnCount: 1));
        var handler = CreateHandler(trackerClient, queue, registry, new StubWorkspaceManager(), timeProvider);

        await handler.ExecuteAsync(workflowOptions, CancellationToken.None);

        var snapshot = Assert.Single(registry.GetActiveSessions());
        Assert.Equal("In Progress", snapshot.IssueState);
        Assert.Equal(["1"], trackerClient.LastRefreshedIssueIds);

        trackedSession.Dispose();
    }

    [Fact]
    public async Task ExecuteAsync_requires_exec_marker_when_enabled()
    {
        var workflowOptions = CreateWorkflowOptions(requireExecMarker: true);
        var trackerClient = new StubIssueTrackerClient
        {
            CandidateIssues =
            [
                CreateIssue("1", "ABC-1", state: "Todo", labels: ["exec:agent"]),
                CreateIssue("2", "ABC-2", state: "Todo")
            ]
        };
        var logger = new TestLogger<OrchestratorPollingIterationHandler>();
        var queue = CreateQueue(workflowOptions);
        var handler = CreateHandler(trackerClient, queue, CreateRegistry(), new StubWorkspaceManager(), logger: logger);

        await handler.ExecuteAsync(workflowOptions, CancellationToken.None);

        var queuedIssues = queue.GetSnapshot().Queued.Select(snapshot => snapshot.IssueIdentifier).ToArray();
        Assert.Equal(["ABC-1"], queuedIssues);

        var skippedEntry = Assert.Single(
            logger.Entries,
            entry => entry.Message.Contains("poll_dispatch skipped", StringComparison.Ordinal)
                && Equals(entry.State["issue_identifier"], "ABC-2"));
        Assert.Equal("missing_exec_marker", Assert.IsType<string>(skippedEntry.State["reason"]));
    }

    [Fact]
    public async Task ExecuteAsync_skips_issues_with_configured_dispatch_block_labels()
    {
        var workflowOptions = CreateWorkflowOptions(dispatchBlockLabels: ["backlog", "human-review"]);
        var trackerClient = new StubIssueTrackerClient
        {
            CandidateIssues =
            [
                CreateIssue("1", "ABC-1", state: "Todo", labels: ["Human-Review"]),
                CreateIssue("2", "ABC-2", state: "Todo", labels: ["ready"])
            ]
        };
        var logger = new TestLogger<OrchestratorPollingIterationHandler>();
        var queue = CreateQueue(workflowOptions);
        var handler = CreateHandler(trackerClient, queue, CreateRegistry(), new StubWorkspaceManager(), logger: logger);

        await handler.ExecuteAsync(workflowOptions, CancellationToken.None);

        var queuedIssues = queue.GetSnapshot().Queued.Select(snapshot => snapshot.IssueIdentifier).ToArray();
        Assert.Equal(["ABC-2"], queuedIssues);

        var skippedEntry = Assert.Single(
            logger.Entries,
            entry => entry.Message.Contains("poll_dispatch skipped", StringComparison.Ordinal)
                && Equals(entry.State["issue_identifier"], "ABC-1"));
        Assert.Equal("blocked_by_label", Assert.IsType<string>(skippedEntry.State["reason"]));
    }

    [Fact]
    public async Task ExecuteAsync_ignores_exec_marker_when_requirement_disabled()
    {
        var workflowOptions = CreateWorkflowOptions(requireExecMarker: false);
        var trackerClient = new StubIssueTrackerClient
        {
            CandidateIssues =
            [
                CreateIssue("1", "ABC-1", state: "Todo")
            ]
        };
        var queue = CreateQueue(workflowOptions);
        var handler = CreateHandler(trackerClient, queue, CreateRegistry(), new StubWorkspaceManager());

        await handler.ExecuteAsync(workflowOptions, CancellationToken.None);

        var queuedIssues = queue.GetSnapshot().Queued.Select(snapshot => snapshot.IssueIdentifier).ToArray();
        Assert.Equal(["ABC-1"], queuedIssues);
    }

    [Fact]
    public async Task ExecuteAsync_uses_custom_exec_marker()
    {
        var workflowOptions = CreateWorkflowOptions(requireExecMarker: true, execMarker: "run:codex");
        var trackerClient = new StubIssueTrackerClient
        {
            CandidateIssues =
            [
                CreateIssue("1", "ABC-1", state: "Todo", labels: ["exec:agent"]),
                CreateIssue("2", "ABC-2", state: "Todo", labels: ["run:codex"])
            ]
        };
        var queue = CreateQueue(workflowOptions);
        var handler = CreateHandler(trackerClient, queue, CreateRegistry(), new StubWorkspaceManager());

        await handler.ExecuteAsync(workflowOptions, CancellationToken.None);

        var queuedIssues = queue.GetSnapshot().Queued.Select(snapshot => snapshot.IssueIdentifier).ToArray();
        Assert.Equal(["ABC-2"], queuedIssues);
    }

    private static OrchestratorPollingIterationHandler CreateHandler(
        IIssueTrackerClient issueTrackerClient,
        OrchestratorDispatchQueue dispatchQueue,
        ActiveSessionRegistry activeSessionRegistry,
        IWorkspaceManager workspaceManager,
        TimeProvider? timeProvider = null,
        Microsoft.Extensions.Logging.ILogger<OrchestratorPollingIterationHandler>? logger = null)
    {
        return new OrchestratorPollingIterationHandler(
            issueTrackerClient,
            dispatchQueue,
            activeSessionRegistry,
            workspaceManager,
            timeProvider ?? TimeProvider.System,
            logger ?? NullLogger<OrchestratorPollingIterationHandler>.Instance);
    }

    private static OrchestratorDispatchQueue CreateQueue(
        WorkflowServiceOptions workflowOptions,
        TimeProvider? timeProvider = null)
    {
        return new OrchestratorDispatchQueue(
            new StaticWorkflowOptionsProvider(workflowOptions),
            new RetryDelayPlanner(() => 1d),
            timeProvider ?? TimeProvider.System,
            NullLogger<OrchestratorDispatchQueue>.Instance);
    }

    private static ActiveSessionRegistry CreateRegistry(TimeProvider? timeProvider = null)
    {
        return new ActiveSessionRegistry(
            timeProvider ?? TimeProvider.System,
            NullLogger<ActiveSessionRegistry>.Instance);
    }

    private static WorkflowServiceOptions CreateWorkflowOptions(
        int maxConcurrentAgents = 4,
        IReadOnlyDictionary<string, int>? maxConcurrentAgentsByState = null,
        int stallTimeoutMs = 300_000,
        bool requireExecMarker = false,
        string execMarker = "exec:agent",
        IReadOnlyList<string>? dispatchBlockLabels = null)
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
                ["Todo", "In Progress"],
                ["Done", "Canceled"])
            {
                DispatchBlockLabels = dispatchBlockLabels ?? Array.Empty<string>()
            },
            new WorkflowPollingOptions(1_000),
            new WorkflowWorkspaceOptions(Path.Combine(Path.GetTempPath(), "symphony-tests")),
            new WorkflowHookOptions(
                null,
                null,
                null,
                null,
                60_000),
            new WorkflowAgentOptions(
                maxConcurrentAgents,
                20,
                300_000,
                maxConcurrentAgentsByState ?? new Dictionary<string, int>(StringComparer.Ordinal),
                requireExecMarker,
                execMarker),
            new WorkflowCodexOptions(
                "codex app-server",
                null,
                null,
                null,
                3_600_000,
                5_000,
                stallTimeoutMs));
    }

    private static Issue CreateIssue(
        string id,
        string identifier,
        string state,
        int? priority = 1,
        DateTimeOffset? createdAt = null,
        IReadOnlyList<IssueBlocker>? blockedBy = null,
        IReadOnlyList<string>? labels = null)
    {
        return new Issue(
            id,
            identifier,
            $"Issue {identifier}",
            description: "Polling iteration handler test",
            priority: priority,
            state: state,
            labels: labels,
            blockedBy: blockedBy,
            createdAt: createdAt ?? new DateTimeOffset(2026, 3, 16, 12, 0, 0, TimeSpan.Zero));
    }

    private static async Task RunUntilCanceledAsync(ActiveSessionRegistry.TrackedActiveSession trackedSession)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, trackedSession.CancellationToken);
        }
        catch (OperationCanceledException) when (trackedSession.CancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            trackedSession.Dispose();
        }
    }

    private sealed class StaticWorkflowOptionsProvider(WorkflowServiceOptions workflowOptions) : IWorkflowOptionsProvider
    {
        public Task<WorkflowServiceOptions> GetCurrentAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(workflowOptions);
        }
    }

    private sealed class StubIssueTrackerClient : IIssueTrackerClient
    {
        public IReadOnlyList<Issue> CandidateIssues { get; set; } = Array.Empty<Issue>();

        public IReadOnlyList<Issue> RefreshedIssues { get; set; } = Array.Empty<Issue>();

        public int FetchCandidateIssuesCalls { get; private set; }

        public IReadOnlyList<string> LastRefreshedIssueIds { get; private set; } = Array.Empty<string>();

        public Task<IReadOnlyList<Issue>> FetchCandidateIssuesAsync(CancellationToken cancellationToken = default)
        {
            FetchCandidateIssuesCalls++;
            return Task.FromResult(CandidateIssues);
        }

        public Task<IReadOnlyList<Issue>> FetchIssuesByStatesAsync(
            IReadOnlyCollection<string> stateNames,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Issue>>(Array.Empty<Issue>());
        }

        public Task<IReadOnlyList<Issue>> FetchIssueStatesByIdsAsync(
            IReadOnlyCollection<string> issueIds,
            CancellationToken cancellationToken = default)
        {
            LastRefreshedIssueIds = issueIds.ToArray();
            return Task.FromResult(RefreshedIssues);
        }
    }

    private sealed class StubWorkspaceManager : IWorkspaceManager
    {
        public List<string> DeletedIssueIdentifiers { get; } = [];

        public Task<Workspace> CreateForIssueAsync(string issueIdentifier, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new Workspace(Path.Combine(Path.GetTempPath(), issueIdentifier), issueIdentifier, createdNow: true));
        }

        public Task DeleteForIssueAsync(string issueIdentifier, CancellationToken cancellationToken = default)
        {
            DeletedIssueIdentifiers.Add(issueIdentifier);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTimeProvider(DateTimeOffset currentTime) : TimeProvider
    {
        private DateTimeOffset _currentTime = currentTime;

        public override DateTimeOffset GetUtcNow()
        {
            return _currentTime;
        }

        public void SetUtcNow(DateTimeOffset currentTime)
        {
            _currentTime = currentTime;
        }
    }
}
