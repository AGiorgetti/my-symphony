using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Symphony.Application.Orchestration;
using Symphony.Host.Configuration;
using Symphony.Host.Dashboard;

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
    public void RecordActivity_trims_history_to_500_entries_and_appends_warning()
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

        Assert.Equal(500, activities.Count);
        Assert.DoesNotContain(activities, activity => activity.Title == "Message 0");
        Assert.DoesNotContain(activities, activity => activity.Title == "Message 1");
        Assert.Equal("Message 500", activities[^2].Title);
        Assert.Equal(SessionActivityKind.Warning, activities[^1].Kind);
        Assert.Equal("Activity history trimmed", activities[^1].Title);
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
