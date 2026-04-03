using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Symphony.Application.Orchestration;
using Symphony.Host.Configuration;
using Symphony.Host.Dashboard;
using Symphony.Domain.Sessions;

namespace Symphony.Host.IntegrationTests;

public sealed class SessionActivityStoreTests
{
    [Fact]
    public void RecordSessionStart_and_end_updates_lifecycle_views()
    {
        var store = CreateStore();
        var startedAt = new DateTimeOffset(2026, 3, 19, 14, 0, 0, TimeSpan.Zero);
        var endedAt = startedAt.AddMinutes(7);

        store.RecordSessionStart("ABC-1", startedAt, "https://github.com/AGiorgetti/my-symphony/issues/81");
        store.RecordSessionEnd("ABC-1", endedAt, "Succeeded");

        var session = Assert.Single(store.GetAllSessions());
        Assert.Equal("ABC-1", session.IssueIdentifier);
        Assert.Equal("https://github.com/AGiorgetti/my-symphony/issues/81", session.IssueUrl);
        Assert.Equal(startedAt, session.StartedAt);
        Assert.Equal(endedAt, session.EndedAt);
        Assert.Equal("Succeeded", session.FinalOutcome);
        Assert.False(session.IsActive);
        Assert.Empty(store.GetActiveSessions());
        Assert.Single(store.GetEndedSessions());
    }

    [Fact]
    public void RecordActivity_persists_entries_for_a_session()
    {
        var store = CreateStore();
        var timestamp = new DateTimeOffset(2026, 3, 19, 14, 5, 0, TimeSpan.Zero);

        store.RecordSessionStart("ABC-2", timestamp.AddMinutes(-1));
        store.RecordActivity("ABC-2", new SessionActivityEntry(SessionActivityKind.ProgressUpdate, timestamp, "Turn 2", "Applied changes"));

        var activity = Assert.Single(store.GetActivities("ABC-2"));
        Assert.Equal(SessionActivityKind.ProgressUpdate, activity.Kind);
        Assert.Equal("Turn 2", activity.Title);
        Assert.Equal("Applied changes", activity.Detail);
    }

    [Fact]
    public void Debug_transcript_sink_records_debug_entries_for_a_session()
    {
        var store = CreateStore(trackAgentMessageDeltas: true);
        var timestamp = new DateTimeOffset(2026, 3, 19, 14, 6, 0, TimeSpan.Zero);

        store.RecordSessionStart("ABC-2", timestamp.AddMinutes(-1));
        ((IAgentDebugTranscriptSink)store).RecordOutbound("ABC-2", timestamp, "Sent turn/start", "{\"method\":\"turn/start\"}");

        var activity = Assert.Single(store.GetActivities("ABC-2"));
        Assert.Equal(SessionActivityKind.DebugMessage, activity.Kind);
        Assert.Equal("Sent turn/start", activity.Title);
        Assert.Equal("{\"method\":\"turn/start\"}", activity.Detail);
    }

    [Fact]
    public void SessionActivityStore_exposes_delta_tracking_flag_from_dashboard_options()
    {
        var enabledStore = CreateStore(trackAgentMessageDeltas: true);
        var disabledStore = CreateStore(trackAgentMessageDeltas: false);

        Assert.True(((IAgentDebugTranscriptSink)enabledStore).TrackAgentMessageDeltas);
        Assert.False(((IAgentDebugTranscriptSink)disabledStore).TrackAgentMessageDeltas);
    }

    [Fact]
    public void RecordActivity_preserves_full_activity_history()
    {
        var store = CreateStore();
        var baseTimestamp = new DateTimeOffset(2026, 3, 19, 15, 0, 0, TimeSpan.Zero);

        store.RecordSessionStart("ABC-3", baseTimestamp);

        for (var index = 0; index < 501; index++)
        {
            store.RecordActivity(
                "ABC-3",
                new SessionActivityEntry(
                    SessionActivityKind.AgentMessage,
                    baseTimestamp.AddSeconds(index),
                    $"Message {index}",
                    null));
        }

        var activities = store.GetActivities("ABC-3");

        Assert.Equal(501, activities.Count);
        Assert.Equal("Message 0", activities[0].Title);
        Assert.Equal("Message 500", activities[^1].Title);
        Assert.DoesNotContain(activities, activity => activity.Title == "Activity history trimmed");
    }

    [Fact]
    public async Task Concurrent_reads_and_writes_do_not_throw_and_keep_store_consistent()
    {
        var store = CreateStore();
        var startedAt = new DateTimeOffset(2026, 3, 19, 16, 0, 0, TimeSpan.Zero);

        store.RecordSessionStart("ABC-4", startedAt);

        var writerTasks = Enumerable.Range(0, 24)
            .Select(
                index => Task.Run(
                    () => store.RecordActivity(
                        "ABC-4",
                        new SessionActivityEntry(
                            SessionActivityKind.AgentMessage,
                            startedAt.AddSeconds(index),
                            $"Message {index}",
                            "parallel write"))))
            .ToArray();
        var readerTasks = Enumerable.Range(0, 24)
            .Select(
                readerIndex => Task.Run(
                    () =>
                    {
                        _ = readerIndex;
                        _ = store.GetAllSessions();
                        _ = store.GetActiveSessions();
                        _ = store.GetEndedSessions();
                        _ = store.GetSession("ABC-4");
                        _ = store.GetActivities("ABC-4");
                    }))
            .ToArray();

        await Task.WhenAll(writerTasks.Concat(readerTasks));

        var session = Assert.Single(store.GetActiveSessions());
        Assert.Equal("ABC-4", session.IssueIdentifier);
        Assert.Equal(24, store.GetActivities("ABC-4").Count);
    }

    [Fact]
    public void RecordActivity_attaches_single_request_estimate_only_to_supported_debug_messages()
    {
        var store = CreateStore();
        var startedAt = new DateTimeOffset(2026, 3, 19, 16, 30, 0, TimeSpan.Zero);

        store.RecordSessionStart("ABC-5", startedAt);
        store.RecordActivity(
            "ABC-5",
            new SessionActivityEntry(
                SessionActivityKind.DebugMessage,
                startedAt.AddSeconds(1),
                "Sent turn/start",
                "{\"method\":\"turn/start\",\"params\":{\"input\":[{\"type\":\"text\",\"text\":\"Prompt body for estimation\"}]}}"));
        store.RecordActivity(
            "ABC-5",
            new SessionActivityEntry(
                SessionActivityKind.DebugMessage,
                startedAt.AddSeconds(2),
                "Received item/started",
                "{\"method\":\"item/started\",\"params\":{\"item\":{\"id\":\"item-1\",\"type\":\"userMessage\",\"text\":\"Prompt body for estimation\"}}}"));
        store.RecordActivity(
            "ABC-5",
            new SessionActivityEntry(
                SessionActivityKind.DebugMessage,
                startedAt.AddSeconds(3),
                "Received item/completed",
                "{\"method\":\"item/completed\",\"params\":{\"item\":{\"id\":\"item-2\",\"type\":\"agentMessage\",\"content\":[{\"type\":\"output_text\",\"text\":\"Assistant reply payload\"}]}}}"));
        store.RecordActivity(
            "ABC-5",
            new SessionActivityEntry(
                SessionActivityKind.DebugMessage,
                startedAt.AddSeconds(4),
                "Received thread/tokenUsage/updated",
                "{\"method\":\"thread/tokenUsage/updated\"}"));

        store.RecordSessionMetadata(
            "ABC-5",
            startedAt.AddSeconds(4),
            new LiveSessionMetadata(
                "thread-5",
                "turn-5",
                codexInputTokens: 120,
                codexOutputTokens: 25,
                codexTotalTokens: 145,
                estimatedInputTokens: 118,
                estimatedOutputTokens: 20,
                estimatedTotalTokens: 138,
                lastReportedInputTokens: 120,
                lastReportedCachedInputTokens: 44,
                lastReportedOutputTokens: 25,
                lastReportedReasoningTokens: 7,
                lastReportedTotalTokens: 145,
                tokenComparisonStatus: SessionTokenComparisonStatus.Mismatch,
                tokenInputDelta: 2,
                tokenOutputDelta: 5,
                tokenTotalDelta: 7,
                lastEstimatedTokenAt: startedAt,
                lastReportedTokenAt: startedAt.AddSeconds(1),
                lastUsageOperation: new SessionTokenUsageOperation(
                    "turn-5:thread_tokenUsage_updated:145",
                    "thread_tokenUsage_updated",
                    startedAt.AddSeconds(1),
                    1,
                    120,
                    44,
                    25,
                    7,
                    145),
                turnCount: 1),
            attempt: 1,
            orchestratorSessionId: "orch-5");

        var activities = store.GetActivities("ABC-5");
        Assert.Equal(4, activities.Count);

        Assert.NotNull(activities[0].TokenUsage);
        Assert.Equal("per-entry-estimate", activities[0].TokenUsage!.Source);
        Assert.True(activities[0].TokenUsage!.EstimatedInputTokens > 0);
        Assert.Equal(0, activities[0].TokenUsage!.EstimatedOutputTokens);

        Assert.NotNull(activities[1].TokenUsage);
        Assert.Equal("per-entry-estimate", activities[1].TokenUsage!.Source);
        Assert.True(activities[1].TokenUsage!.EstimatedInputTokens > 0);
        Assert.Equal(0, activities[1].TokenUsage!.EstimatedOutputTokens);

        Assert.NotNull(activities[2].TokenUsage);
        Assert.Equal("per-entry-estimate", activities[2].TokenUsage!.Source);
        Assert.Equal(0, activities[2].TokenUsage!.EstimatedInputTokens);
        Assert.True(activities[2].TokenUsage!.EstimatedOutputTokens > 0);

        Assert.Null(activities[3].TokenUsage);
    }

    private static SessionActivityStore CreateStore(bool trackAgentMessageDeltas = false)
    {
        return new SessionActivityStore(
            NullLogger<SessionActivityStore>.Instance,
            Options.Create(
                new DashboardUiOptions
                {
                    TrackAgentMessageDeltas = trackAgentMessageDeltas
                }));
    }
}
