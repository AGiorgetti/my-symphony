using Microsoft.Extensions.Logging.Abstractions;
using Symphony.Application.Orchestration;
using Symphony.Application.Tests.Logging;
using Symphony.Domain.Issues;
using Symphony.Domain.Runs;
using Symphony.Domain.Sessions;

namespace Symphony.Application.Tests.Orchestration;

public sealed class ActiveSessionRegistryTests
{
    [Fact]
    public void BeginSession_tracks_active_sessions_by_normalized_issue_id()
    {
        var registry = CreateRegistry();
        using var trackedSession = registry.BeginSession(CreateIssue("AbC-123", "ABC-123"), attempt: null, CancellationToken.None);

        Assert.True(registry.TryCancelForReconciliation("abc-123", "Done"));
        Assert.True(trackedSession.WasCanceledByReconciliation);
        Assert.True(trackedSession.CancellationToken.IsCancellationRequested);

        var snapshot = Assert.Single(registry.GetActiveSessions());
        Assert.Equal("AbC-123", snapshot.IssueId);
        Assert.Equal(RunAttemptStatus.CanceledByReconciliation, snapshot.Status);
    }

    [Fact]
    public void TryCancelForReconciliation_cancels_matching_session_only()
    {
        var registry = CreateRegistry();
        using var firstSession = registry.BeginSession(CreateIssue("ABC-1", "ABC-1"), attempt: null, CancellationToken.None);
        using var secondSession = registry.BeginSession(CreateIssue("ABC-2", "ABC-2"), attempt: 2, CancellationToken.None);

        Assert.True(registry.TryCancelForReconciliation("abc-2", "Canceled"));
        Assert.False(firstSession.CancellationToken.IsCancellationRequested);
        Assert.True(secondSession.CancellationToken.IsCancellationRequested);

        var snapshots = registry.GetActiveSessions().OrderBy(snapshot => snapshot.IssueIdentifier).ToArray();
        Assert.Equal(RunAttemptStatus.InitializingSession, snapshots[0].Status);
        Assert.Equal(RunAttemptStatus.CanceledByReconciliation, snapshots[1].Status);
    }

    [Fact]
    public void ExecutionContext_updates_session_metadata_and_status_snapshot()
    {
        var registry = CreateRegistry();
        using var trackedSession = registry.BeginSession(CreateIssue("ABC-9", "ABC-9"), attempt: 1, CancellationToken.None);
        var context = trackedSession.CreateExecutionContext();

        context.UpdateStatus(RunAttemptStatus.StreamingTurn);
        context.UpdateSession(
            new LiveSessionMetadata(
                "thread-9",
                "turn-1",
                codexAppServerPid: "1234",
                lastCodexEvent: "turn_started",
                lastCodexTimestamp: DateTimeOffset.UtcNow,
                lastCodexMessage: "Session started",
                codexInputTokens: 10,
                codexOutputTokens: 20,
                codexTotalTokens: 30,
                lastReportedInputTokens: 10,
                lastReportedOutputTokens: 20,
                lastReportedTotalTokens: 30,
                turnCount: 1));

        var snapshot = Assert.Single(registry.GetActiveSessions());

        Assert.Equal(RunAttemptStatus.StreamingTurn, snapshot.Status);
        Assert.NotNull(snapshot.Session);
        Assert.Equal("thread-9-turn-1", snapshot.Session!.SessionId);
        Assert.Equal(1, snapshot.Session.TurnCount);
    }

    [Fact]
    public void ExecutionContext_updates_issue_snapshot_when_tracker_state_changes()
    {
        var registry = CreateRegistry();
        using var trackedSession = registry.BeginSession(CreateIssue("ABC-9", "ABC-9"), attempt: 1, CancellationToken.None);
        var context = trackedSession.CreateExecutionContext();

        context.UpdateIssue(
            new Issue(
                "ABC-9",
                "ABC-9",
                "Issue ABC-9",
                description: "Active session registry test",
                state: "Done",
                createdAt: DateTimeOffset.UtcNow));

        var snapshot = Assert.Single(registry.GetActiveSessions());

        Assert.Equal("Done", snapshot.IssueState);
    }

    [Fact]
    public void ExecutionContext_logs_issue_and_session_fields_with_spec_names()
    {
        var logger = new TestLogger<ActiveSessionRegistry>();
        var registry = new ActiveSessionRegistry(TimeProvider.System, logger);
        using var trackedSession = registry.BeginSession(CreateIssue("ABC-10", "ABC-10"), attempt: 3, CancellationToken.None);
        var context = trackedSession.CreateExecutionContext();

        context.UpdateSession(
            new LiveSessionMetadata(
                "thread-10",
                "turn-2",
                codexAppServerPid: "5678",
                lastCodexEvent: "turn_started",
                lastCodexTimestamp: DateTimeOffset.UtcNow,
                lastCodexMessage: "streaming",
                codexInputTokens: 11,
                codexOutputTokens: 12,
                codexTotalTokens: 23,
                lastReportedInputTokens: 11,
                lastReportedOutputTokens: 12,
                lastReportedTotalTokens: 23,
                turnCount: 2));

        var updateEntry = Assert.Single(
            logger.Entries,
            entry => entry.Message.Contains("session_tracking updated", StringComparison.Ordinal));

        Assert.Equal("ABC-10", Assert.IsType<string>(updateEntry.State["issue_id"]));
        Assert.Equal("ABC-10", Assert.IsType<string>(updateEntry.State["issue_identifier"]));
        Assert.Equal("thread-10-turn-2", Assert.IsType<string>(updateEntry.State["session_id"]));
    }

    private static ActiveSessionRegistry CreateRegistry()
    {
        return new ActiveSessionRegistry(TimeProvider.System, NullLogger<ActiveSessionRegistry>.Instance);
    }

    private static Issue CreateIssue(string id, string identifier)
    {
        return new Issue(
            id,
            identifier,
            $"Issue {identifier}",
            description: "Active session registry test",
            state: "In Progress",
            createdAt: DateTimeOffset.UtcNow);
    }
}
