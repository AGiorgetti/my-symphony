using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Symphony.Abstractions.Orchestration;
using Symphony.Application.Configuration;
using Symphony.Application.Orchestration;
using Symphony.Application.Polling;
using Symphony.Application.Runtime;
using Symphony.Application.Tests.Logging;
using Symphony.Domain.Issues;
using Symphony.Domain.Runs;

namespace Symphony.Application.Tests.Orchestration;

public sealed class OrchestratorDispatchQueueTests
{
    [Fact]
    public async Task QueueAsync_tracks_queued_running_and_retry_state_for_status_readers()
    {
        var queue = CreateQueue(maxConcurrentAgents: 1);
        var worker = new BlockingQueuedIssueWorker();
        var registry = CreateRegistry();
        var hostedService = CreateHostedService(queue, registry, worker);
        var issue = CreateIssue("1", "ABC-1");

        var enqueueResult = await queue.QueueAsync(issue);

        Assert.Equal(DispatchEnqueueResult.Enqueued, enqueueResult);
        Assert.Single(queue.GetSnapshot().Queued);

        await hostedService.StartAsync(CancellationToken.None);
        await worker.ExecutionStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var runningSnapshot = queue.GetSnapshot();

        Assert.Empty(runningSnapshot.Queued);
        Assert.Single(runningSnapshot.Running);
        Assert.Equal("ABC-1", runningSnapshot.Running[0].IssueIdentifier);

        worker.AllowCompletion("ABC-1");
        await worker.WaitForCompletionAsync("ABC-1", TimeSpan.FromSeconds(2));
        await WaitForConditionAsync(() => queue.GetSnapshot().Retrying.Count == 1, TimeSpan.FromSeconds(2));
        await hostedService.StopAsync(CancellationToken.None);

        var completedSnapshot = queue.GetSnapshot();
        var retry = Assert.Single(completedSnapshot.Retrying);

        Assert.Empty(completedSnapshot.Queued);
        Assert.Empty(completedSnapshot.Running);
        Assert.Equal(1, retry.Attempt);
        Assert.Equal("ABC-1", retry.IssueIdentifier);
        Assert.Null(retry.Error);
        Assert.Equal(1, completedSnapshot.MaxConcurrentAgents);
        Assert.Equal(1, completedSnapshot.AvailableSlots);
    }

    [Fact]
    public async Task QueueAsync_uses_concurrency_gate_to_reject_work_when_capacity_is_exhausted()
    {
        var queue = CreateQueue(maxConcurrentAgents: 1);
        var worker = new BlockingQueuedIssueWorker();
        var registry = CreateRegistry();
        var hostedService = CreateHostedService(queue, registry, worker);

        await hostedService.StartAsync(CancellationToken.None);

        var firstResult = await queue.QueueAsync(CreateIssue("1", "ABC-1"));
        await worker.ExecutionStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var secondResult = await queue.QueueAsync(CreateIssue("2", "ABC-2"));

        Assert.Equal(DispatchEnqueueResult.Enqueued, firstResult);
        Assert.Equal(DispatchEnqueueResult.NoCapacity, secondResult);
        Assert.Equal(0, queue.GetSnapshot().AvailableSlots);

        worker.AllowCompletion("ABC-1");
        await worker.WaitForCompletionAsync("ABC-1", TimeSpan.FromSeconds(2));
        await hostedService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task QueueAsync_refreshes_max_concurrency_for_future_dispatches()
    {
        var optionsProvider = new MutableWorkflowOptionsProvider(CreateWorkflowOptions(maxConcurrentAgents: 1));
        var queue = CreateQueue(optionsProvider);
        var firstWorker = new BlockingQueuedIssueWorker();
        var registry = CreateRegistry();
        var firstHostedService = CreateHostedService(queue, registry, firstWorker);

        await firstHostedService.StartAsync(CancellationToken.None);
        await queue.QueueAsync(CreateIssue("1", "ABC-1"));
        await firstWorker.ExecutionStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        optionsProvider.Current = CreateWorkflowOptions(maxConcurrentAgents: 2);

        var secondResult = await queue.QueueAsync(CreateIssue("2", "ABC-2"));

        Assert.Equal(DispatchEnqueueResult.Enqueued, secondResult);
        Assert.Equal(2, queue.GetSnapshot().MaxConcurrentAgents);

        firstWorker.AllowCompletion("ABC-1");
        await firstWorker.WaitForCompletionAsync("ABC-1", TimeSpan.FromSeconds(2));
        await firstHostedService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task QueueAsync_returns_already_claimed_for_duplicate_issue_ids()
    {
        var queue = CreateQueue(maxConcurrentAgents: 2);

        var firstResult = await queue.QueueAsync(CreateIssue("1", "ABC-1"));
        var secondResult = await queue.QueueAsync(CreateIssue("1", "ABC-1"));

        Assert.Equal(DispatchEnqueueResult.Enqueued, firstResult);
        Assert.Equal(DispatchEnqueueResult.AlreadyClaimed, secondResult);
        Assert.Single(queue.GetSnapshot().Queued);
    }

    [Fact]
    public async Task DispatchWorker_uses_active_session_registry_for_targeted_cancellation()
    {
        var queue = CreateQueue(maxConcurrentAgents: 2);
        var registry = CreateRegistry();
        var attemptHistoryTracker = new AttemptHistoryTracker();
        var worker = new BlockingQueuedIssueWorker();
        var hostedService = CreateHostedService(queue, registry, attemptHistoryTracker, worker);

        await hostedService.StartAsync(CancellationToken.None);
        await queue.QueueAsync(CreateIssue("ABC-1", "ABC-1"));
        await queue.QueueAsync(CreateIssue("ABC-2", "ABC-2"));
        await worker.WaitForStartsAsync(2, TimeSpan.FromSeconds(2));

        Assert.True(registry.TryCancelForReconciliation("abc-2", "Done"));
        await worker.WaitForCancellationAsync("ABC-2", TimeSpan.FromSeconds(2));

        Assert.DoesNotContain(
            worker.CancellationLog,
            entry => string.Equals(entry, "ABC-1", StringComparison.Ordinal));
        Assert.Contains(
            registry.GetActiveSessions(),
            session => session.IssueIdentifier == "ABC-1" && session.Status == RunAttemptStatus.InitializingSession);

        worker.AllowCompletion("ABC-1");
        await worker.WaitForCompletionAsync("ABC-1", TimeSpan.FromSeconds(2));
        await hostedService.StopAsync(CancellationToken.None);
        Assert.Empty(registry.GetActiveSessions());
    }

    [Fact]
    public async Task QueueAsync_logs_issue_identifiers_for_enqueue_execution_and_retry()
    {
        var logger = new TestLogger<OrchestratorDispatchQueue>();
        var queue = CreateQueue(new StaticWorkflowOptionsProvider(CreateWorkflowOptions(maxConcurrentAgents: 1)), logger: logger);
        var registry = CreateRegistry();
        var attemptHistoryTracker = new AttemptHistoryTracker();
        var worker = new BlockingQueuedIssueWorker();
        var hostedService = CreateHostedService(queue, registry, attemptHistoryTracker, worker);
        var issue = CreateIssue("123", "ABC-123");

        await hostedService.StartAsync(CancellationToken.None);
        await queue.QueueAsync(issue);
        await worker.ExecutionStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var enqueueEntry = Assert.Single(
            logger.Entries,
            entry => entry.Message.Contains("dispatch_enqueue completed", StringComparison.Ordinal));
        var startEntry = Assert.Single(
            logger.Entries,
            entry => entry.Message.Contains("dispatch_execution started", StringComparison.Ordinal));

        Assert.Equal("123", Assert.IsType<string>(enqueueEntry.State["issue_id"]));
        Assert.Equal("ABC-123", Assert.IsType<string>(enqueueEntry.State["issue_identifier"]));
        Assert.Equal("123", Assert.IsType<string>(startEntry.State["issue_id"]));
        Assert.Equal("ABC-123", Assert.IsType<string>(startEntry.State["issue_identifier"]));

        worker.AllowCompletion("ABC-123");
        await worker.WaitForCompletionAsync("ABC-123", TimeSpan.FromSeconds(2));
        await WaitForConditionAsync(() => queue.GetSnapshot().Retrying.Count == 1, TimeSpan.FromSeconds(2));
        await hostedService.StopAsync(CancellationToken.None);

        var retryEntry = Assert.Single(
            logger.Entries,
            entry => entry.Message.Contains("dispatch_retry scheduled", StringComparison.Ordinal));
        var completedEntry = Assert.Single(
            logger.Entries,
            entry => entry.Message.Contains("dispatch_execution completed", StringComparison.Ordinal));

        Assert.Equal("123", Assert.IsType<string>(retryEntry.State["issue_id"]));
        Assert.Equal("ABC-123", Assert.IsType<string>(retryEntry.State["issue_identifier"]));
        Assert.Equal(1, Assert.IsType<int>(retryEntry.State["attempt"]));
        Assert.Equal("continuation", Assert.IsType<string>(retryEntry.State["reason"]));
        Assert.Null(retryEntry.State["error"]);
        Assert.Equal("123", Assert.IsType<string>(completedEntry.State["issue_id"]));
        Assert.Equal("ABC-123", Assert.IsType<string>(completedEntry.State["issue_identifier"]));
    }

    [Fact]
    public async Task DispatchWorker_schedules_failure_retry_for_transient_exception()
    {
        var logger = new TestLogger<OrchestratorDispatchQueue>();
        var queue = CreateQueue(
            new StaticWorkflowOptionsProvider(CreateWorkflowOptions(maxConcurrentAgents: 1)),
            logger: logger);
        var registry = CreateRegistry();
        var attemptHistoryTracker = new AttemptHistoryTracker();
        var worker = new ThrowingQueuedIssueWorker(new InvalidOperationException("transient failure"));
        var hostedService = CreateHostedService(queue, registry, attemptHistoryTracker, worker);

        await hostedService.StartAsync(CancellationToken.None);
        await queue.QueueAsync(CreateIssue("1", "ABC-1"));
        await worker.ExecutionAttempted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitForConditionAsync(() => queue.GetSnapshot().Retrying.Count == 1, TimeSpan.FromSeconds(2));
        await hostedService.StopAsync(CancellationToken.None);

        var retry = Assert.Single(queue.GetSnapshot().Retrying);
        var retryEntry = Assert.Single(
            logger.Entries,
            entry => entry.Message.Contains("dispatch_retry scheduled", StringComparison.Ordinal)
                && string.Equals(entry.State["reason"] as string, "failure", StringComparison.Ordinal));

        Assert.Equal(1, retry.Attempt);
        Assert.Equal("ABC-1", retry.IssueIdentifier);
        Assert.Equal("transient failure", retry.Error);
        Assert.Equal(1, queue.GetSnapshot().AvailableSlots);
        Assert.Equal("1", Assert.IsType<string>(retryEntry.State["issue_id"]));
        Assert.Equal("ABC-1", Assert.IsType<string>(retryEntry.State["issue_identifier"]));
        Assert.Equal("transient failure", Assert.IsType<string>(retryEntry.State["error"]));
    }

    [Fact]
    public async Task DispatchWorker_releases_claim_after_non_transient_failure()
    {
        var queue = CreateQueue(maxConcurrentAgents: 1);
        var registry = CreateRegistry();
        var attemptHistoryTracker = new AttemptHistoryTracker();
        var worker = new ThrowingQueuedIssueWorker(new NonTransientIssueExecutionException("do not retry"));
        var hostedService = CreateHostedService(queue, registry, attemptHistoryTracker, worker);
        var issue = CreateIssue("1", "ABC-1");

        await hostedService.StartAsync(CancellationToken.None);
        await queue.QueueAsync(issue);
        await worker.ExecutionAttempted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await hostedService.StopAsync(CancellationToken.None);

        var enqueueResult = await queue.QueueAsync(issue);

        Assert.Equal(DispatchEnqueueResult.Enqueued, enqueueResult);
        Assert.Single(queue.GetSnapshot().Queued);
    }

    [Fact]
    public async Task RetryDispatchBackgroundService_dispatches_due_retry_attempts()
    {
        var timeProvider = TimeProvider.System;
        var queue = CreateQueue(
            new StaticWorkflowOptionsProvider(CreateWorkflowOptions(maxConcurrentAgents: 1, maxRetryBackoffMs: 25)),
            new RetryDelayPlanner(() => 1d),
            timeProvider);
        var registry = CreateRegistry(timeProvider);
        var attemptHistoryTracker = new AttemptHistoryTracker();
        var worker = new FailOnceQueuedIssueWorker();
        var dispatchHostedService = CreateHostedService(queue, registry, attemptHistoryTracker, worker, timeProvider);
        var retryHostedService = CreateRetryHostedService(queue, timeProvider);

        await dispatchHostedService.StartAsync(CancellationToken.None);
        await retryHostedService.StartAsync(CancellationToken.None);
        await queue.QueueAsync(CreateIssue("1", "ABC-1"));
        await worker.RetryExecutionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await retryHostedService.StopAsync(CancellationToken.None);
        await dispatchHostedService.StopAsync(CancellationToken.None);

        Assert.Equal(new int?[] { null, 1 }, worker.Attempts.ToArray());
    }

    [Fact]
    public async Task DispatchWorker_waits_for_resume_before_starting_queued_work()
    {
        var queue = CreateQueue(maxConcurrentAgents: 1);
        var registry = CreateRegistry();
        var worker = new BlockingQueuedIssueWorker();
        var executionGate = CreateExecutionGate(initialState: OrchestratorControlState.Stopped);
        var hostedService = CreateHostedService(
            queue,
            registry,
            new AttemptHistoryTracker(),
            worker,
            timeProvider: null,
            executionGate);

        await hostedService.StartAsync(CancellationToken.None);
        var enqueueResult = await queue.QueueAsync(CreateIssue("1", "ABC-1"));

        Assert.Equal(DispatchEnqueueResult.Enqueued, enqueueResult);
        await Task.Delay(150);
        Assert.False(worker.ExecutionStarted.Task.IsCompleted);
        Assert.Single(queue.GetSnapshot().Queued);

        await executionGate.ResumeAsync();
        await worker.ExecutionStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        worker.AllowCompletion("ABC-1");
        await worker.WaitForCompletionAsync("ABC-1", TimeSpan.FromSeconds(2));
        await hostedService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RetryDispatchBackgroundService_waits_for_resume_before_dispatching_due_retry_attempts()
    {
        var timeProvider = TimeProvider.System;
        var queue = CreateQueue(
            new StaticWorkflowOptionsProvider(CreateWorkflowOptions(maxConcurrentAgents: 1, maxRetryBackoffMs: 25)),
            new RetryDelayPlanner(() => 1d),
            timeProvider);
        var registry = CreateRegistry(timeProvider);
        var attemptHistoryTracker = new AttemptHistoryTracker();
        var worker = new FailOnceQueuedIssueWorker();
        var executionGate = CreateExecutionGate(timeProvider, OrchestratorControlState.Stopped);
        var dispatchHostedService = CreateHostedService(queue, registry, attemptHistoryTracker, worker, timeProvider, executionGate);
        var retryHostedService = CreateRetryHostedService(queue, timeProvider, executionGate);

        await dispatchHostedService.StartAsync(CancellationToken.None);
        await retryHostedService.StartAsync(CancellationToken.None);
        await queue.QueueAsync(CreateIssue("1", "ABC-1"));

        await Task.Delay(150);
        Assert.Empty(worker.Attempts);

        await executionGate.ResumeAsync();
        await worker.RetryExecutionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await retryHostedService.StopAsync(CancellationToken.None);
        await dispatchHostedService.StopAsync(CancellationToken.None);

        Assert.Equal(new int?[] { null, 1 }, worker.Attempts.ToArray());
    }

    [Fact]
    public async Task DispatchWorker_records_successful_attempts_for_dashboard_history()
    {
        var timeProvider = TimeProvider.System;
        var queue = CreateQueue(maxConcurrentAgents: 1);
        var registry = CreateRegistry(timeProvider);
        var attemptHistoryTracker = new AttemptHistoryTracker();
        var worker = new BlockingQueuedIssueWorker();
        var hostedService = CreateHostedService(queue, registry, attemptHistoryTracker, worker, timeProvider);

        await hostedService.StartAsync(CancellationToken.None);
        await queue.QueueAsync(CreateIssue("1", "ABC-1"));
        await worker.ExecutionStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        worker.AllowCompletion("ABC-1");
        await worker.WaitForCompletionAsync("ABC-1", TimeSpan.FromSeconds(2));
        await WaitForConditionAsync(() => attemptHistoryTracker.GetRecentAttempts().Count == 1, TimeSpan.FromSeconds(2));
        await hostedService.StopAsync(CancellationToken.None);

        var attempt = Assert.Single(attemptHistoryTracker.GetRecentAttempts());
        Assert.Equal("ABC-1", attempt.IssueIdentifier);
        Assert.Equal("Succeeded", attempt.Outcome);
        Assert.Null(attempt.Error);
    }

    [Fact]
    public async Task DispatchWorker_records_retrying_attempts_for_dashboard_history()
    {
        var timeProvider = TimeProvider.System;
        var queue = CreateQueue(maxConcurrentAgents: 1);
        var registry = CreateRegistry(timeProvider);
        var attemptHistoryTracker = new AttemptHistoryTracker();
        var worker = new ThrowingQueuedIssueWorker(new InvalidOperationException("transient failure"));
        var hostedService = CreateHostedService(queue, registry, attemptHistoryTracker, worker, timeProvider);

        await hostedService.StartAsync(CancellationToken.None);
        await queue.QueueAsync(CreateIssue("1", "ABC-1"));
        await worker.ExecutionAttempted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitForConditionAsync(() => attemptHistoryTracker.GetRecentAttempts().Count == 1, TimeSpan.FromSeconds(2));
        await hostedService.StopAsync(CancellationToken.None);

        var attempt = Assert.Single(attemptHistoryTracker.GetRecentAttempts());
        Assert.Equal("Retrying", attempt.Outcome);
        Assert.Equal("transient failure", attempt.Error);
    }

    [Fact]
    public async Task DispatchWorker_records_timeout_attempts_and_schedules_retry()
    {
        var timeProvider = TimeProvider.System;
        var queue = CreateQueue(maxConcurrentAgents: 1);
        var registry = CreateRegistry(timeProvider);
        var attemptHistoryTracker = new AttemptHistoryTracker();
        var worker = new ThrowingQueuedIssueWorker(
            new IssueExecutionTimedOutException("worker timed out", new TimeoutException("timed out")));
        var hostedService = CreateHostedService(queue, registry, attemptHistoryTracker, worker, timeProvider);

        await hostedService.StartAsync(CancellationToken.None);
        await queue.QueueAsync(CreateIssue("1", "ABC-1"));
        await worker.ExecutionAttempted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitForConditionAsync(() => queue.GetSnapshot().Retrying.Count == 1, TimeSpan.FromSeconds(2));
        await WaitForConditionAsync(() => attemptHistoryTracker.GetRecentAttempts().Count == 1, TimeSpan.FromSeconds(2));
        await hostedService.StopAsync(CancellationToken.None);

        var attempt = Assert.Single(attemptHistoryTracker.GetRecentAttempts());
        var retry = Assert.Single(queue.GetSnapshot().Retrying);

        Assert.Equal("TimedOut", attempt.Outcome);
        Assert.Equal("worker timed out", attempt.Error);
        Assert.Equal("worker timed out", retry.Error);
    }

    [Fact]
    public async Task DispatchWorker_records_stalled_attempts_and_schedules_retry()
    {
        var timeProvider = TimeProvider.System;
        var queue = CreateQueue(maxConcurrentAgents: 1);
        var registry = CreateRegistry(timeProvider);
        var attemptHistoryTracker = new AttemptHistoryTracker();
        var worker = new BlockingQueuedIssueWorker();
        var hostedService = CreateHostedService(queue, registry, attemptHistoryTracker, worker, timeProvider);

        await hostedService.StartAsync(CancellationToken.None);
        await queue.QueueAsync(CreateIssue("1", "ABC-1"));
        await worker.ExecutionStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var stalled = await registry.TryMarkStalledAndWaitAsync(
            "1",
            "Session stalled after 360000 ms of Codex inactivity.",
            CancellationToken.None);

        await WaitForConditionAsync(() => queue.GetSnapshot().Retrying.Count == 1, TimeSpan.FromSeconds(2));
        await WaitForConditionAsync(() => attemptHistoryTracker.GetRecentAttempts().Count == 1, TimeSpan.FromSeconds(2));
        await hostedService.StopAsync(CancellationToken.None);

        Assert.True(stalled);

        var attempt = Assert.Single(attemptHistoryTracker.GetRecentAttempts());
        var retry = Assert.Single(queue.GetSnapshot().Retrying);

        Assert.Equal("Stalled", attempt.Outcome);
        Assert.Equal("Session stalled after 360000 ms of Codex inactivity.", attempt.Error);
        Assert.Equal("Session stalled after 360000 ms of Codex inactivity.", retry.Error);
    }

    private static OrchestratorDispatchQueue CreateQueue(int maxConcurrentAgents)
    {
        return CreateQueue(new StaticWorkflowOptionsProvider(CreateWorkflowOptions(maxConcurrentAgents)));
    }

    private static OrchestratorDispatchQueue CreateQueue(IWorkflowOptionsProvider workflowOptionsProvider)
    {
        return CreateQueue(workflowOptionsProvider, retryDelayPlanner: null, timeProvider: null, logger: null);
    }

    private static OrchestratorDispatchQueue CreateQueue(
        IWorkflowOptionsProvider workflowOptionsProvider,
        ILogger<OrchestratorDispatchQueue> logger)
    {
        return CreateQueue(workflowOptionsProvider, retryDelayPlanner: null, timeProvider: null, logger: logger);
    }

    private static OrchestratorDispatchQueue CreateQueue(
        IWorkflowOptionsProvider workflowOptionsProvider,
        RetryDelayPlanner? retryDelayPlanner = null,
        TimeProvider? timeProvider = null,
        ILogger<OrchestratorDispatchQueue>? logger = null)
    {
        return new OrchestratorDispatchQueue(
            workflowOptionsProvider,
            retryDelayPlanner ?? new RetryDelayPlanner(() => 1d),
            timeProvider ?? TimeProvider.System,
            logger ?? NullLogger<OrchestratorDispatchQueue>.Instance);
    }

    private static DispatchWorkerBackgroundService CreateHostedService(
        OrchestratorDispatchQueue queue,
        ActiveSessionRegistry registry,
        IQueuedIssueWorker queuedIssueWorker)
    {
        return CreateHostedService(queue, registry, new AttemptHistoryTracker(), queuedIssueWorker, timeProvider: null);
    }

    private static DispatchWorkerBackgroundService CreateHostedService(
        OrchestratorDispatchQueue queue,
        ActiveSessionRegistry registry,
        AttemptHistoryTracker attemptHistoryTracker,
        IQueuedIssueWorker queuedIssueWorker,
        TimeProvider? timeProvider = null,
        IOrchestratorExecutionGate? executionGate = null)
    {
        return new DispatchWorkerBackgroundService(
            queue,
            executionGate ?? CreateExecutionGate(timeProvider),
            registry,
            attemptHistoryTracker,
            timeProvider ?? TimeProvider.System,
            queuedIssueWorker,
            NullLogger<DispatchWorkerBackgroundService>.Instance);
    }

    private static RetryDispatchBackgroundService CreateRetryHostedService(
        OrchestratorDispatchQueue queue,
        TimeProvider? timeProvider = null,
        IOrchestratorExecutionGate? executionGate = null)
    {
        return new RetryDispatchBackgroundService(
            queue,
            executionGate ?? CreateExecutionGate(timeProvider),
            timeProvider ?? TimeProvider.System,
            NullLogger<RetryDispatchBackgroundService>.Instance);
    }

    private static ActiveSessionRegistry CreateRegistry(TimeProvider? timeProvider = null)
    {
        return new ActiveSessionRegistry(
            timeProvider ?? TimeProvider.System,
            NullLogger<ActiveSessionRegistry>.Instance);
    }

    private static WorkflowServiceOptions CreateWorkflowOptions(
        int maxConcurrentAgents,
        int maxRetryBackoffMs = 300_000)
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
                ["Todo"],
                ["Done"]),
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
                maxRetryBackoffMs,
                new Dictionary<string, int>(StringComparer.Ordinal),
                false,
                "exec:agent"),
            new WorkflowCodexOptions(
                "codex app-server",
                null,
                null,
                null,
                3_600_000,
                5_000,
                300_000));
    }

    private static Issue CreateIssue(string id, string identifier)
    {
        return new Issue(
            id,
            identifier,
            $"Issue {identifier}",
            description: "Dispatch queue test",
            state: "Todo",
            createdAt: DateTimeOffset.UtcNow);
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(condition(), "Timed out waiting for the expected condition.");
    }

    private sealed class StaticWorkflowOptionsProvider(WorkflowServiceOptions workflowOptions) : IWorkflowOptionsProvider
    {
        public Task<WorkflowServiceOptions> GetCurrentAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(workflowOptions);
        }
    }

    private sealed class MutableWorkflowOptionsProvider(WorkflowServiceOptions current) : IWorkflowOptionsProvider
    {
        public WorkflowServiceOptions Current { get; set; } = current;

        public Task<WorkflowServiceOptions> GetCurrentAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Current);
        }
    }

    private static OrchestratorControlService CreateExecutionGate(
        TimeProvider? timeProvider = null,
        OrchestratorControlState initialState = OrchestratorControlState.Started)
    {
        var resolvedTimeProvider = timeProvider ?? TimeProvider.System;

        return new OrchestratorControlService(
            Options.Create(
                new OrchestratorControlOptions
                {
                    InitialState = initialState.ToString()
                }),
            new PollingRefreshTrigger(resolvedTimeProvider),
            resolvedTimeProvider,
            NullLogger<OrchestratorControlService>.Instance);
    }

    private sealed class BlockingQueuedIssueWorker : IQueuedIssueWorker
    {
        private readonly Lock _lock = new();
        private readonly Dictionary<string, TaskCompletionSource> _allowCompletionByIssue = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TaskCompletionSource> _completionByIssue = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TaskCompletionSource> _cancellationByIssue = new(StringComparer.Ordinal);
        private readonly TaskCompletionSource _allStarts = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _startCount;

        public List<string> CancellationLog { get; } = [];

        public TaskCompletionSource ExecutionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ExecuteAsync(QueuedIssueExecutionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            lock (_lock)
            {
                _allowCompletionByIssue[context.Issue.Identifier] = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _completionByIssue[context.Issue.Identifier] = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _cancellationByIssue[context.Issue.Identifier] = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            var currentCount = Interlocked.Increment(ref _startCount);
            ExecutionStarted.TrySetResult();
            if (currentCount >= 2)
            {
                _allStarts.TrySetResult();
            }

            try
            {
                await GetAllowCompletion(context.Issue.Identifier).Task.WaitAsync(context.CancellationToken);
                GetCompletion(context.Issue.Identifier).TrySetResult();
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                lock (_lock)
                {
                    CancellationLog.Add(context.Issue.Identifier);
                }

                GetCancellation(context.Issue.Identifier).TrySetResult();
                throw;
            }
        }

        public void AllowCompletion(string issueIdentifier)
        {
            GetAllowCompletion(issueIdentifier).TrySetResult();
        }

        public Task WaitForStartsAsync(int expectedCount, TimeSpan timeout)
        {
            return expectedCount <= 1
                ? ExecutionStarted.Task.WaitAsync(timeout)
                : _allStarts.Task.WaitAsync(timeout);
        }

        public Task WaitForCancellationAsync(string issueIdentifier, TimeSpan timeout)
        {
            return GetCancellation(issueIdentifier).Task.WaitAsync(timeout);
        }

        public Task WaitForCompletionAsync(string issueIdentifier, TimeSpan timeout)
        {
            return GetCompletion(issueIdentifier).Task.WaitAsync(timeout);
        }

        private TaskCompletionSource GetAllowCompletion(string issueIdentifier)
        {
            lock (_lock)
            {
                return _allowCompletionByIssue[issueIdentifier];
            }
        }

        private TaskCompletionSource GetCompletion(string issueIdentifier)
        {
            lock (_lock)
            {
                return _completionByIssue[issueIdentifier];
            }
        }

        private TaskCompletionSource GetCancellation(string issueIdentifier)
        {
            lock (_lock)
            {
                return _cancellationByIssue[issueIdentifier];
            }
        }
    }

    private sealed class ThrowingQueuedIssueWorker(Exception exception) : IQueuedIssueWorker
    {
        public TaskCompletionSource ExecutionAttempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ExecuteAsync(QueuedIssueExecutionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            ExecutionAttempted.TrySetResult();
            return Task.FromException(exception);
        }
    }

    private sealed class FailOnceQueuedIssueWorker : IQueuedIssueWorker
    {
        private int _executionCount;

        public ConcurrentQueue<int?> Attempts { get; } = new();

        public TaskCompletionSource RetryExecutionCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ExecuteAsync(QueuedIssueExecutionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            Attempts.Enqueue(context.Attempt);

            if (Interlocked.Increment(ref _executionCount) == 1)
            {
                return Task.FromException(new InvalidOperationException("transient failure"));
            }

            RetryExecutionCompleted.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
