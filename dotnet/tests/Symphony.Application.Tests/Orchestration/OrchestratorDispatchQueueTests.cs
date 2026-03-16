using Microsoft.Extensions.Logging.Abstractions;
using Symphony.Abstractions.Orchestration;
using Symphony.Application.Configuration;
using Symphony.Application.Orchestration;
using Symphony.Domain.Issues;
using Symphony.Domain.Runs;

namespace Symphony.Application.Tests.Orchestration;

public sealed class OrchestratorDispatchQueueTests
{
    [Fact]
    public async Task QueueAsync_tracks_queued_and_running_state_for_status_readers()
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
        await worker.ExecutionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await hostedService.StopAsync(CancellationToken.None);

        var completedSnapshot = queue.GetSnapshot();

        Assert.Empty(completedSnapshot.Queued);
        Assert.Empty(completedSnapshot.Running);
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
        await worker.ExecutionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
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
        await firstWorker.ExecutionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
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
        var worker = new BlockingQueuedIssueWorker();
        var hostedService = CreateHostedService(queue, registry, worker);

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

    private static OrchestratorDispatchQueue CreateQueue(int maxConcurrentAgents)
    {
        return CreateQueue(new StaticWorkflowOptionsProvider(CreateWorkflowOptions(maxConcurrentAgents)));
    }

    private static OrchestratorDispatchQueue CreateQueue(IWorkflowOptionsProvider workflowOptionsProvider)
    {
        return new OrchestratorDispatchQueue(
            workflowOptionsProvider,
            TimeProvider.System,
            NullLogger<OrchestratorDispatchQueue>.Instance);
    }

    private static DispatchWorkerBackgroundService CreateHostedService(
        OrchestratorDispatchQueue queue,
        ActiveSessionRegistry registry,
        IQueuedIssueWorker queuedIssueWorker)
    {
        return new DispatchWorkerBackgroundService(
            queue,
            registry,
            queuedIssueWorker,
            NullLogger<DispatchWorkerBackgroundService>.Instance);
    }

    private static ActiveSessionRegistry CreateRegistry()
    {
        return new ActiveSessionRegistry(TimeProvider.System, NullLogger<ActiveSessionRegistry>.Instance);
    }

    private static WorkflowServiceOptions CreateWorkflowOptions(int maxConcurrentAgents)
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
                300_000,
                new Dictionary<string, int>(StringComparer.Ordinal)),
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

        public TaskCompletionSource ExecutionCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
                ExecutionCompleted.TrySetResult();
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
}
