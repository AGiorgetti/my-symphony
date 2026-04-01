using Microsoft.Extensions.Logging.Abstractions;
using Symphony.Abstractions.Orchestration;
using Symphony.Application.Configuration;
using Symphony.Application.Orchestration;
using Symphony.Application.Polling;
using Symphony.Application.Runtime;
using Symphony.Domain.Issues;
using Symphony.Domain.Sessions;

namespace Symphony.Application.Tests.Runtime;

public sealed class OrchestratorRuntimeServiceTests
{
    [Fact]
    public async Task GetStateSnapshotAsync_returns_running_retrying_and_live_totals()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero));
        var registry = new ActiveSessionRegistry(timeProvider, NullLogger<ActiveSessionRegistry>.Instance);
        var queue = CreateQueue(timeProvider);
        var refreshTrigger = new PollingRefreshTrigger(timeProvider);
        using var trackedSession = registry.BeginSession(CreateIssue("1", "ABC-1"), attempt: 2, CancellationToken.None);
        trackedSession.CreateExecutionContext().UpdateSession(
            new LiveSessionMetadata(
                "thread-1",
                "turn-2",
                codexAppServerPid: "1234",
                lastCodexEvent: "turn_completed",
                lastCodexTimestamp: timeProvider.GetUtcNow(),
                lastCodexMessage: "Done",
                codexInputTokens: 100,
                codexOutputTokens: 40,
                codexTotalTokens: 140,
                lastReportedInputTokens: 100,
                lastReportedOutputTokens: 40,
                lastReportedTotalTokens: 140,
                turnCount: 2));
        await queue.QueueAsync(CreateIssue("2", "ABC-2"), attempt: 3);
        await queue.ScheduleFailureRetryAsync(
            new OrchestratorDispatchQueue.DispatchQueueWorkItem(CreateIssue("2", "ABC-2"), 2, timeProvider.GetUtcNow()),
            new InvalidOperationException("retry later"),
            CancellationToken.None);
        var service = new OrchestratorRuntimeService(registry, queue, refreshTrigger, timeProvider);

        var snapshot = await service.GetStateSnapshotAsync();

        Assert.Single(snapshot.Running);
        Assert.Single(snapshot.Retrying);
        Assert.Equal(100, snapshot.CodexTotals.InputTokens);
        Assert.Equal(40, snapshot.CodexTotals.OutputTokens);
        Assert.Equal(140, snapshot.CodexTotals.TotalTokens);
        Assert.True(snapshot.CodexTotals.SecondsRunning >= 0d);
    }

    [Fact]
    public async Task GetIssueSnapshotAsync_returns_retrying_issue_details_when_present()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero));
        var registry = new ActiveSessionRegistry(timeProvider, NullLogger<ActiveSessionRegistry>.Instance);
        var queue = CreateQueue(timeProvider);
        var service = new OrchestratorRuntimeService(registry, queue, new PollingRefreshTrigger(timeProvider), timeProvider);
        await queue.QueueAsync(CreateIssue("2", "ABC-2"), attempt: 3);
        await queue.ScheduleFailureRetryAsync(
            new OrchestratorDispatchQueue.DispatchQueueWorkItem(CreateIssue("2", "ABC-2"), 2, timeProvider.GetUtcNow()),
            new InvalidOperationException("retry later"),
            CancellationToken.None);

        var snapshot = await service.GetIssueSnapshotAsync("abc-2");

        Assert.NotNull(snapshot);
        Assert.Equal("retrying", snapshot!.Status);
        Assert.Equal("ABC-2", snapshot.IssueIdentifier);
        Assert.NotNull(snapshot.Retry);
        Assert.Equal("retry later", snapshot.LastError);
        Assert.Equal(3, snapshot.CurrentRetryAttempt);
        Assert.Equal(2, snapshot.RestartCount);
    }

    [Fact]
    public void RequestRefresh_returns_queue_receipt_and_coalesces_pending_requests()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero));
        var service = new OrchestratorRuntimeService(
            new ActiveSessionRegistry(timeProvider, NullLogger<ActiveSessionRegistry>.Instance),
            CreateQueue(timeProvider),
            new PollingRefreshTrigger(timeProvider),
            timeProvider);

        var firstRequest = service.RequestRefresh();
        var secondRequest = service.RequestRefresh();

        Assert.True(firstRequest.Queued);
        Assert.False(firstRequest.Coalesced);
        Assert.True(secondRequest.Queued);
        Assert.True(secondRequest.Coalesced);
        Assert.Equal(["poll", "reconcile"], secondRequest.Operations);
    }

    [Fact]
    public async Task GetIssueSnapshotAsync_returns_blocked_issue_details_and_follow_up_actions_when_present()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero));
        var registry = new ActiveSessionRegistry(timeProvider, NullLogger<ActiveSessionRegistry>.Instance);
        var queue = CreateQueue(timeProvider);
        var followUpActionRegistry = new FollowUpActionRegistry(timeProvider);
        var issue = CreateIssue("2", "ABC-2");
        var followUpAction = followUpActionRegistry.CreatePending(
            issue.Id,
            issue.Identifier,
            "orch-123",
            BlockingReasonCode.ManualDecisionRequired,
            "Need a human choice",
            "Review the requested manual decision, then resolve the follow-up action to resume the run.",
            [new FollowUpActionOptionSnapshot("resume", "Resume", "Continue after review.")]);
        queue.BlockAsync(
            issue,
            "orch-123",
            attempt: 2,
            BlockingReasonCode.ManualDecisionRequired,
            "Need a human choice",
            "Review the requested manual decision, then resolve the follow-up action to resume the run.",
            followUpAction.FollowUpActionId);
        var service = new OrchestratorRuntimeService(
            registry,
            queue,
            new PollingRefreshTrigger(timeProvider),
            timeProvider,
            followUpActionRegistry);

        var stateSnapshot = await service.GetStateSnapshotAsync();
        var issueSnapshot = await service.GetIssueSnapshotAsync("abc-2");

        var blocked = Assert.Single(stateSnapshot.Blocked ?? []);
        var action = Assert.Single(stateSnapshot.FollowUpActions ?? []);
        Assert.Equal("ABC-2", blocked.IssueIdentifier);
        Assert.Equal("orch-123", blocked.OrchestratorSessionId);
        Assert.Equal(followUpAction.FollowUpActionId, blocked.FollowUpActionId);
        Assert.Equal(FollowUpActionStatus.Pending, action.Status);

        Assert.NotNull(issueSnapshot);
        Assert.Equal("blocked_error", issueSnapshot!.Status);
        Assert.Equal("orch-123", issueSnapshot.OrchestratorSessionId);
        Assert.Equal(1, issueSnapshot.RestartCount);
        Assert.Equal(2, issueSnapshot.CurrentRetryAttempt);
        Assert.NotNull(issueSnapshot.Blocked);
        Assert.Single(issueSnapshot.FollowUpActions ?? []);
        Assert.Equal("Need a human choice", issueSnapshot.LastError);
    }

    private static OrchestratorDispatchQueue CreateQueue(TimeProvider timeProvider)
    {
        return new OrchestratorDispatchQueue(
            new StaticWorkflowOptionsProvider(CreateWorkflowOptions()),
            new RetryDelayPlanner(() => 1d),
            timeProvider,
            NullLogger<OrchestratorDispatchQueue>.Instance);
    }

    private static WorkflowServiceOptions CreateWorkflowOptions()
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
                2,
                20,
                300_000,
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
            description: "Runtime service test",
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

    private sealed class FakeTimeProvider(DateTimeOffset currentTime) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return currentTime;
        }
    }
}
