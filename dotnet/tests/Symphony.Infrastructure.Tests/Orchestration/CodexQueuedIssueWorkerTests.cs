using System.Text.Json;
using Symphony.Abstractions.Trackers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Symphony.Abstractions.Processes;
using Symphony.Abstractions.Workspaces;
using Symphony.Application.Configuration;
using Symphony.Application.Orchestration;
using Symphony.Domain.Issues;
using Symphony.Domain.Runs;
using Symphony.Domain.Sessions;
using Symphony.Domain.Workflows;
using Symphony.Domain.Workspaces;
using Symphony.Infrastructure.Codex;
using Symphony.Infrastructure.Orchestration;
using Symphony.Infrastructure.Tests.Codex;
using Symphony.Infrastructure.Workflows;

namespace Symphony.Infrastructure.Tests.Orchestration;

public sealed class CodexQueuedIssueWorkerTests
{
    [Fact]
    public async Task ExecuteAsync_builds_prompt_runs_hooks_and_updates_statuses()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "symphony-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        try
        {
            var workflowDefinition = new WorkflowDefinition(
                config: null,
                promptTemplate: """
                    Issue {{ issue.identifier }}
                    {% if attempt %}
                    Attempt {{ attempt }}
                    {% endif %}
                    """);
            var sessionFactory = new TestCodexProcessSessionFactory(
                async (line, session) =>
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    var method = root.TryGetProperty("method", out var methodElement)
                        ? methodElement.GetString()
                        : null;

                    if (method == "initialize")
                    {
                        session.EnqueueStdout(new { id = 1, result = new { } });
                    }
                    else if (method == "thread/start")
                    {
                        session.EnqueueStdout(new { id = 2, result = new { thread = new { id = "thread-abc" } } });
                    }
                    else if (method == "turn/start")
                    {
                        session.EnqueueStdout(new { id = 3, result = new { turn = new { id = "turn-xyz" } } });
                        session.EnqueueStdout(new { method = "turn/completed", @params = new { message = "done" } });
                    }

                    await Task.CompletedTask;
                });
            var client = CreateClient(sessionFactory);
            var processRunner = new RecordingProcessRunner();
            var worker = new CodexQueuedIssueWorker(
                new StaticWorkflowOptionsProvider(CreateWorkflowOptions()),
                new StaticWorkflowDefinitionProvider(workflowDefinition),
                new RecordingIssueTrackerClient(),
                new StaticWorkspaceManager(new Workspace(workspacePath, "GH-21", createdNow: true)),
                processRunner,
                new WorkflowPromptRenderer(),
                client,
                NullLogger<CodexQueuedIssueWorker>.Instance);
            using var testContext = CreateContext(attempt: 3);

            await worker.ExecuteAsync(testContext.Context);

            Assert.Equal(
                [
                    RunAttemptStatus.PreparingWorkspace,
                    RunAttemptStatus.BuildingPrompt,
                    RunAttemptStatus.LaunchingAgentProcess,
                    RunAttemptStatus.InitializingSession,
                    RunAttemptStatus.StreamingTurn,
                    RunAttemptStatus.Finishing
                ],
                testContext.Logger.Entries
                    .Where(entry => entry.Message.Contains("session_status completed", StringComparison.Ordinal))
                    .Select(entry => Assert.IsType<RunAttemptStatus>(entry.State["status"]))
                    .ToArray());
            Assert.Equal(2, processRunner.Requests.Count);
            Assert.All(processRunner.Requests, request => Assert.Equal(workspacePath, request.WorkingDirectory));

            var startRequest = Assert.Single(sessionFactory.Requests);
            Assert.Equal("codex app-server", startRequest.Command);
            Assert.Equal(workspacePath, startRequest.WorkingDirectory);

            Assert.NotNull(sessionFactory.Session);
            var turnStartLine = sessionFactory.Session!.SentLines.Single(line => line.Contains("\"method\":\"turn/start\"", StringComparison.Ordinal));
            using var turnStartDocument = JsonDocument.Parse(turnStartLine);
            var turnStartParams = turnStartDocument.RootElement.GetProperty("params");

            Assert.Equal(workspacePath, turnStartParams.GetProperty("cwd").GetString());
            Assert.Equal("#21: Implement Codex agent runner", turnStartParams.GetProperty("title").GetString());
            Assert.Contains("Issue #21", turnStartParams.GetProperty("input")[0].GetProperty("text").GetString(), StringComparison.Ordinal);
            Assert.Contains("Attempt 3", turnStartParams.GetProperty("input")[0].GetProperty("text").GetString(), StringComparison.Ordinal);

            var latestSession = Assert.IsType<LiveSessionMetadata>(testContext.Context.Session);
            Assert.Equal("thread-abc-turn-xyz", latestSession.SessionId);
            Assert.Equal("turn_completed", latestSession.LastCodexEvent);
        }
        finally
        {
            Directory.Delete(workspacePath, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_fails_fast_when_before_run_hook_fails()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "symphony-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        try
        {
            var sessionFactory = new TestCodexProcessSessionFactory();
            var worker = new CodexQueuedIssueWorker(
                new StaticWorkflowOptionsProvider(CreateWorkflowOptions()),
                new StaticWorkflowDefinitionProvider(new WorkflowDefinition(null, "Prompt")),
                new RecordingIssueTrackerClient(),
                new StaticWorkspaceManager(new Workspace(workspacePath, "GH-21", createdNow: true)),
                new RecordingProcessRunner(exitCodes: [1]),
                new WorkflowPromptRenderer(),
                CreateClient(sessionFactory),
                NullLogger<CodexQueuedIssueWorker>.Instance);

            using var testContext = CreateContext(attempt: null);
            await Assert.ThrowsAsync<InvalidOperationException>(() => worker.ExecuteAsync(testContext.Context));

            Assert.Empty(sessionFactory.Requests);
        }
        finally
        {
            Directory.Delete(workspacePath, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_translates_hook_timeout_to_issue_timeout()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "symphony-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        try
        {
            var worker = new CodexQueuedIssueWorker(
                new StaticWorkflowOptionsProvider(CreateWorkflowOptions()),
                new StaticWorkflowDefinitionProvider(new WorkflowDefinition(null, "Prompt")),
                new RecordingIssueTrackerClient(),
                new StaticWorkspaceManager(new Workspace(workspacePath, "GH-21", createdNow: true)),
                new RecordingProcessRunner(
                    exceptionFactory: request => new ProcessRunTimedOutException(
                        request.FileName,
                        request.WorkingDirectory,
                        request.Timeout ?? TimeSpan.FromMilliseconds(1))),
                new WorkflowPromptRenderer(),
                CreateClient(new TestCodexProcessSessionFactory()),
                NullLogger<CodexQueuedIssueWorker>.Instance);

            using var testContext = CreateContext(attempt: null);

            await Assert.ThrowsAsync<IssueExecutionTimedOutException>(() => worker.ExecuteAsync(testContext.Context));
        }
        finally
        {
            Directory.Delete(workspacePath, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_translates_codex_response_timeout_to_issue_timeout()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "symphony-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        try
        {
            var worker = new CodexQueuedIssueWorker(
                new StaticWorkflowOptionsProvider(CreateWorkflowOptions(beforeRun: null, afterRun: null, readTimeoutMs: 50)),
                new StaticWorkflowDefinitionProvider(new WorkflowDefinition(null, "Prompt")),
                new RecordingIssueTrackerClient(),
                new StaticWorkspaceManager(new Workspace(workspacePath, "GH-21", createdNow: true)),
                new RecordingProcessRunner(),
                new WorkflowPromptRenderer(),
                CreateClient(new TestCodexProcessSessionFactory()),
                NullLogger<CodexQueuedIssueWorker>.Instance);

            using var testContext = CreateContext(attempt: null);

            await Assert.ThrowsAsync<IssueExecutionTimedOutException>(() => worker.ExecuteAsync(testContext.Context));
        }
        finally
        {
            Directory.Delete(workspacePath, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_reuses_same_thread_for_multiple_turns_until_max_turns()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "symphony-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        try
        {
            var workflowDefinition = new WorkflowDefinition(
                config: null,
                promptTemplate: """
                    Issue {{ issue.identifier }}
                    {% if attempt %}
                    Attempt {{ attempt }}
                    {% endif %}
                    """);
            var turnCounter = 0;
            var sessionFactory = new TestCodexProcessSessionFactory(
                async (line, session) =>
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    var method = root.TryGetProperty("method", out var methodElement)
                        ? methodElement.GetString()
                        : null;

                    if (method == "initialize")
                    {
                        session.EnqueueStdout(new { id = 1, result = new { } });
                    }
                    else if (method == "thread/start")
                    {
                        session.EnqueueStdout(new { id = 2, result = new { thread = new { id = "thread-abc" } } });
                    }
                    else if (method == "turn/start")
                    {
                        turnCounter++;
                        session.EnqueueStdout(new { id = turnCounter + 2, result = new { turn = new { id = $"turn-{turnCounter}" } } });
                        session.EnqueueStdout(new { method = "turn/completed", @params = new { message = $"done-{turnCounter}" } });
                    }

                    await Task.CompletedTask;
                });
            var client = CreateClient(sessionFactory);
            var processRunner = new RecordingProcessRunner();
            var issueTrackerClient = new RecordingIssueTrackerClient(
            [
                CreateIssue(state: "Open"),
                CreateIssue(state: "Open")
            ]);
            var worker = new CodexQueuedIssueWorker(
                new StaticWorkflowOptionsProvider(CreateWorkflowOptions(maxTurns: 2)),
                new StaticWorkflowDefinitionProvider(workflowDefinition),
                issueTrackerClient,
                new StaticWorkspaceManager(new Workspace(workspacePath, "GH-21", createdNow: true)),
                processRunner,
                new WorkflowPromptRenderer(),
                client,
                NullLogger<CodexQueuedIssueWorker>.Instance);
            using var testContext = CreateContext(attempt: 3);

            await worker.ExecuteAsync(testContext.Context);

            Assert.Equal(2, issueTrackerClient.Requests.Count);
            Assert.All(issueTrackerClient.Requests, request => Assert.Equal(new[] { "21" }, request));

            Assert.NotNull(sessionFactory.Session);
            var turnStartLines = sessionFactory.Session!.SentLines
                .Where(line => line.Contains("\"method\":\"turn/start\"", StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(2, turnStartLines.Length);

            using var firstTurnDocument = JsonDocument.Parse(turnStartLines[0]);
            using var secondTurnDocument = JsonDocument.Parse(turnStartLines[1]);
            var firstTurnParams = firstTurnDocument.RootElement.GetProperty("params");
            var secondTurnParams = secondTurnDocument.RootElement.GetProperty("params");

            Assert.Equal("thread-abc", firstTurnParams.GetProperty("threadId").GetString());
            Assert.Equal("thread-abc", secondTurnParams.GetProperty("threadId").GetString());
            Assert.Contains("Issue #21", firstTurnParams.GetProperty("input")[0].GetProperty("text").GetString(), StringComparison.Ordinal);
            Assert.Contains("Attempt 3", firstTurnParams.GetProperty("input")[0].GetProperty("text").GetString(), StringComparison.Ordinal);
            Assert.Contains("continuation turn 2 of 2", secondTurnParams.GetProperty("input")[0].GetProperty("text").GetString(), StringComparison.OrdinalIgnoreCase);

            var latestSession = Assert.IsType<LiveSessionMetadata>(testContext.Context.Session);
            Assert.Equal("thread-abc-turn-2", latestSession.SessionId);
            Assert.Equal(2, latestSession.TurnCount);
            Assert.Equal("turn_completed", latestSession.LastCodexEvent);
        }
        finally
        {
            Directory.Delete(workspacePath, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_stops_after_tracker_marks_issue_inactive()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "symphony-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        try
        {
            var sessionFactory = new TestCodexProcessSessionFactory(
                async (line, session) =>
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    var method = root.TryGetProperty("method", out var methodElement)
                        ? methodElement.GetString()
                        : null;

                    if (method == "initialize")
                    {
                        session.EnqueueStdout(new { id = 1, result = new { } });
                    }
                    else if (method == "thread/start")
                    {
                        session.EnqueueStdout(new { id = 2, result = new { thread = new { id = "thread-abc" } } });
                    }
                    else if (method == "turn/start")
                    {
                        session.EnqueueStdout(new { id = 3, result = new { turn = new { id = "turn-1" } } });
                        session.EnqueueStdout(new { method = "turn/completed", @params = new { message = "done-1" } });
                    }

                    await Task.CompletedTask;
                });
            var worker = new CodexQueuedIssueWorker(
                new StaticWorkflowOptionsProvider(CreateWorkflowOptions(beforeRun: null, afterRun: null, maxTurns: 3)),
                new StaticWorkflowDefinitionProvider(new WorkflowDefinition(null, "Prompt")),
                new RecordingIssueTrackerClient([CreateIssue(state: "Done")]),
                new StaticWorkspaceManager(new Workspace(workspacePath, "GH-21", createdNow: true)),
                new RecordingProcessRunner(),
                new WorkflowPromptRenderer(),
                CreateClient(sessionFactory),
                NullLogger<CodexQueuedIssueWorker>.Instance);
            using var testContext = CreateContext(attempt: null);

            await worker.ExecuteAsync(testContext.Context);

            Assert.NotNull(sessionFactory.Session);
            Assert.Single(
                sessionFactory.Session!.SentLines,
                line => line.Contains("\"method\":\"turn/start\"", StringComparison.Ordinal));
            Assert.Equal("Done", testContext.Context.Issue.State);
        }
        finally
        {
            Directory.Delete(workspacePath, recursive: true);
        }
    }

    private static WorkflowServiceOptions CreateWorkflowOptions(
        string? beforeRun = "Write-Host before",
        string? afterRun = "Write-Host after",
        int readTimeoutMs = 5_000,
        int maxTurns = 1)
    {
        return new WorkflowServiceOptions(
            new WorkflowTrackerOptions(
                "github",
                "https://api.github.com",
                "token",
                null,
                "AGiorgetti/my-symphony",
                null,
                null,
                ["open"],
                ["closed"]),
            new WorkflowPollingOptions(5_000),
            new WorkflowWorkspaceOptions(Path.Combine(Path.GetTempPath(), "symphony-runner-tests")),
            new WorkflowHookOptions(
                null,
                beforeRun,
                afterRun,
                null,
                5_000),
            new WorkflowAgentOptions(1, maxTurns, 300_000, new Dictionary<string, int>(StringComparer.Ordinal), false, "exec:agent"),
            new WorkflowCodexOptions(
                "codex app-server",
                "never",
                "workspace-write",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["type"] = "workspaceWrite"
                },
                60_000,
                readTimeoutMs,
                300_000));
    }

    private static TestExecutionContext CreateContext(int? attempt)
    {
        var issue = CreateIssue();
        var logger = new RecordingLogger<ActiveSessionRegistry>();
        var registry = new ActiveSessionRegistry(TimeProvider.System, logger);
        var trackedSession = registry.BeginSession(issue, attempt, CancellationToken.None);

        return new TestExecutionContext(
            trackedSession,
            trackedSession.CreateExecutionContext(),
            logger);
    }

    private static CodexAppServerClient CreateClient(TestCodexProcessSessionFactory sessionFactory)
    {
        return new CodexAppServerClient(
            sessionFactory,
            TimeProvider.System,
            new NoOpTranscriptSink(),
            NullLogger<CodexAppServerClient>.Instance);
    }

    private static Issue CreateIssue(string state = "Todo")
    {
        return new Issue(
            id: "21",
            identifier: "#21",
            title: "Implement Codex agent runner",
            description: "Implement the runner",
            state: state);
    }

    private sealed class StaticWorkflowOptionsProvider(WorkflowServiceOptions options) : IWorkflowOptionsProvider
    {
        public Task<WorkflowServiceOptions> GetCurrentAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(options);
        }
    }

    private sealed class StaticWorkflowDefinitionProvider(WorkflowDefinition definition) : IWorkflowDefinitionProvider
    {
        public Task<WorkflowDefinition> GetCurrentDefinitionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(definition);
        }
    }

    private sealed class StaticWorkspaceManager(Workspace workspace) : IWorkspaceManager
    {
        public Task<Workspace> CreateForIssueAsync(string issueIdentifier, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(workspace);
        }

        public Task DeleteForIssueAsync(string issueIdentifier, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingIssueTrackerClient(IEnumerable<Issue>? refreshedIssues = null) : IIssueTrackerClient
    {
        private readonly Queue<IReadOnlyList<Issue>> _refreshedIssues = new(
            (refreshedIssues ?? [])
            .Select(issue => (IReadOnlyList<Issue>)new[] { issue }));

        public List<IReadOnlyList<string>> Requests { get; } = [];

        public Task<IReadOnlyList<Issue>> FetchCandidateIssuesAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<Issue>> FetchIssuesByStatesAsync(
            IReadOnlyCollection<string> stateNames,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<Issue>> FetchIssueStatesByIdsAsync(
            IReadOnlyCollection<string> issueIds,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(issueIds.ToArray());

            if (_refreshedIssues.Count == 0)
            {
                return Task.FromResult<IReadOnlyList<Issue>>([]);
            }

            return Task.FromResult(_refreshedIssues.Dequeue());
        }
    }

    private sealed class RecordingProcessRunner(
        IReadOnlyList<int>? exitCodes = null,
        Func<ProcessRunRequest, Exception>? exceptionFactory = null) : IProcessRunner
    {
        private readonly Queue<int> _exitCodes = new((exitCodes ?? [0, 0]).ToArray());

        public List<ProcessRunRequest> Requests { get; } = [];

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (exceptionFactory is not null)
            {
                return Task.FromException<ProcessRunResult>(exceptionFactory(request));
            }

            var exitCode = _exitCodes.Count > 0
                ? _exitCodes.Dequeue()
                : 0;

            return Task.FromResult(
                new ProcessRunResult(
                    exitCode,
                    standardOutput: string.Empty,
                    standardError: string.Empty,
                    startedAt: DateTimeOffset.UtcNow,
                    finishedAt: DateTimeOffset.UtcNow));
        }
    }

    private sealed class TestExecutionContext : IDisposable
    {
        private readonly ActiveSessionRegistry.TrackedActiveSession _trackedSession;

        public TestExecutionContext(
            ActiveSessionRegistry.TrackedActiveSession trackedSession,
            QueuedIssueExecutionContext context,
            RecordingLogger<ActiveSessionRegistry> logger)
        {
            _trackedSession = trackedSession;
            Context = context;
            Logger = logger;
        }

        public QueuedIssueExecutionContext Context { get; }

        public RecordingLogger<ActiveSessionRegistry> Logger { get; }

        public void Dispose()
        {
            _trackedSession.Dispose();
        }
    }

    private sealed class RecordingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var structuredState = state as IEnumerable<KeyValuePair<string, object?>>;
            var values = structuredState?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                ?? new Dictionary<string, object?>(StringComparer.Ordinal);

            Entries.Add(new LogEntry(formatter(state, exception), values));
        }
    }

    private sealed record LogEntry(string Message, IReadOnlyDictionary<string, object?> State);

    private sealed class NoOpTranscriptSink : IAgentDebugTranscriptSink
    {
        public bool TrackAgentMessageDeltas => false;

        public void RecordOutbound(string issueIdentifier, DateTimeOffset timestamp, string title, string payload)
        {
        }

        public void RecordInbound(string issueIdentifier, DateTimeOffset timestamp, string title, string payload)
        {
        }

        public void RecordDiagnostic(string issueIdentifier, DateTimeOffset timestamp, string title, string detail)
        {
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
