using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Symphony.Application.Orchestration;
using Symphony.Host.Configuration;
using Symphony.Domain.Sessions;

namespace Symphony.Host.Dashboard;

public sealed class SessionActivityStore(
    ILogger<SessionActivityStore> logger,
    IOptions<DashboardUiOptions>? dashboardUiOptions = null) : ISessionActivityStore, IAgentDebugTranscriptSink
{
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public bool TrackAgentMessageDeltas => dashboardUiOptions?.Value.TrackAgentMessageDeltas ?? false;

    public void RecordSessionStart(string issueIdentifier, DateTimeOffset startedAt, string? issueUrl = null)
    {
        ExecuteWrite(
            issueIdentifier,
            () =>
            {
                _sessions.AddOrUpdate(
                    issueIdentifier,
                    _ => new SessionState(
                        new SessionRecord(issueIdentifier, issueUrl, startedAt, null, null, null, true),
                        ImmutableList<SessionActivityEntry>.Empty,
                        null),
                    (_, existing) => existing with
                    {
                        Record = existing.Record with
                        {
                            IssueUrl = issueUrl ?? existing.Record.IssueUrl,
                            StartedAt = startedAt,
                            EndedAt = null,
                            FinalOutcome = null,
                            FinalError = null,
                            IsActive = true
                        }
                    });
            });
    }

    public void RecordActivity(string issueIdentifier, SessionActivityEntry activity)
    {
        ExecuteWrite(
            issueIdentifier,
            () =>
            {
                _sessions.AddOrUpdate(
                    issueIdentifier,
                    _ => new SessionState(
                        new SessionRecord(issueIdentifier, null, activity.Timestamp, null, null, null, true),
                        ImmutableList<SessionActivityEntry>.Empty.Add(activity),
                        null),
                    (_, existing) => existing with
                    {
                        Activities = existing.Activities.Add(EnrichNewActivity(activity))
                    });
            });
    }

    public void RecordSessionEnd(string issueIdentifier, DateTimeOffset endedAt, string outcome, string? error = null)
    {
        ExecuteWrite(
            issueIdentifier,
            () =>
            {
                _sessions.AddOrUpdate(
                    issueIdentifier,
                    _ => new SessionState(
                        new SessionRecord(issueIdentifier, null, endedAt, endedAt, outcome, error, false),
                        ImmutableList<SessionActivityEntry>.Empty,
                        null),
                    (_, existing) => existing with
                    {
                        Record = existing.Record with
                        {
                            EndedAt = endedAt,
                            FinalOutcome = outcome,
                            FinalError = error,
                            IsActive = false
                        },
                        Metadata = existing.Metadata is null
                            ? null
                            : existing.Metadata with
                            {
                                AvailabilityMessage = "Finished sessions keep the last known session ID and attempt when available. Live token counters are only available while the session is active."
                            }
                    });
            });
    }

    public void RecordSessionMetadata(
        string issueIdentifier,
        DateTimeOffset timestamp,
        LiveSessionMetadata session,
        int? attempt,
        string orchestratorSessionId)
    {
        ExecuteWrite(
            issueIdentifier,
            () =>
            {
                _sessions.AddOrUpdate(
                    issueIdentifier,
                    _ =>
                    {
                        var metadata = BuildMetadataSnapshot(session, attempt, orchestratorSessionId);
                        var activities = EnrichActivitiesWithTokenUsage(
                            ImmutableList<SessionActivityEntry>.Empty,
                            previousMetadata: null,
                            metadata);
                        return new SessionState(
                            new SessionRecord(issueIdentifier, null, timestamp, null, null, null, true),
                            activities,
                            metadata);
                    },
                    (_, existing) =>
                    {
                        var metadata = BuildMetadataSnapshot(session, attempt, orchestratorSessionId);
                        var activities = EnrichActivitiesWithTokenUsage(
                            existing.Activities,
                            existing.Metadata,
                            metadata);
                        return existing with
                        {
                            Metadata = metadata,
                            Activities = activities
                        };
                    });
            });
    }

    public void RecordOutbound(string issueIdentifier, DateTimeOffset timestamp, string title, string payload)
    {
        RecordActivity(issueIdentifier, new SessionActivityEntry(SessionActivityKind.DebugMessage, timestamp, title, payload));
    }

    public void RecordInbound(string issueIdentifier, DateTimeOffset timestamp, string title, string payload)
    {
        RecordActivity(issueIdentifier, new SessionActivityEntry(SessionActivityKind.DebugMessage, timestamp, title, payload));
    }

    public void RecordDiagnostic(string issueIdentifier, DateTimeOffset timestamp, string title, string detail)
    {
        RecordActivity(issueIdentifier, new SessionActivityEntry(SessionActivityKind.DebugMessage, timestamp, title, detail));
    }

    public IReadOnlyList<SessionRecord> GetAllSessions()
    {
        return _sessions.Values
            .Select(state => state.Record)
            .OrderByDescending(record => record.StartedAt)
            .ToArray();
    }

    public IReadOnlyList<DashboardSessionHistorySnapshot> GetAllSessionHistories()
    {
        return _sessions.Values
            .Select(CreateHistorySnapshot)
            .OrderByDescending(history => history.Session.StartedAt)
            .ToArray();
    }

    public IReadOnlyList<SessionRecord> GetActiveSessions()
    {
        return _sessions.Values
            .Select(state => state.Record)
            .Where(record => record.IsActive)
            .OrderByDescending(record => record.StartedAt)
            .ToArray();
    }

    public IReadOnlyList<SessionRecord> GetEndedSessions()
    {
        return _sessions.Values
            .Select(state => state.Record)
            .Where(record => !record.IsActive)
            .OrderByDescending(record => record.EndedAt ?? record.StartedAt)
            .ToArray();
    }

    public SessionRecord? GetSession(string issueIdentifier)
    {
        return _sessions.TryGetValue(issueIdentifier, out var state)
            ? state.Record
            : null;
    }

    public IReadOnlyList<SessionActivityEntry> GetActivities(string issueIdentifier)
    {
        return _sessions.TryGetValue(issueIdentifier, out var state)
            ? state.Activities
            : [];
    }

    public DashboardSessionHistorySnapshot? GetSessionHistory(string issueIdentifier)
    {
        return _sessions.TryGetValue(issueIdentifier, out var state)
            ? CreateHistorySnapshot(state)
            : null;
    }

    private void ExecuteWrite(string issueIdentifier, Action action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issueIdentifier);

        try
        {
            action();
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Failed to update session activity state for {IssueIdentifier}.", issueIdentifier);
        }
    }

    void IAgentDebugTranscriptSink.RecordSessionMetadata(
        string issueIdentifier,
        DateTimeOffset timestamp,
        LiveSessionMetadata session,
        int? attempt,
        string orchestratorSessionId)
    {
        RecordSessionMetadata(issueIdentifier, timestamp, session, attempt, orchestratorSessionId);
    }

    private static DashboardSessionHistorySnapshot CreateHistorySnapshot(SessionState state)
    {
        return new DashboardSessionHistorySnapshot(state.Record, state.Activities, state.Metadata);
    }

    private static DashboardSessionMetadataSnapshot BuildMetadataSnapshot(
        LiveSessionMetadata session,
        int? attempt,
        string orchestratorSessionId)
    {
        var tokenUsage = new DashboardSessionTokenUsageSnapshot(
            session.CodexInputTokens,
            session.CodexOutputTokens,
            session.CodexTotalTokens,
            session.LastReportedInputTokens,
            session.LastReportedCachedInputTokens,
            session.LastReportedOutputTokens,
            session.LastReportedReasoningTokens,
            session.LastReportedTotalTokens,
            session.LastReportedTokenAt,
            session.LastUsageOperation is null
                ? null
                : new DashboardSessionTokenOperationSnapshot(
                    session.LastUsageOperation.OperationId,
                    session.LastUsageOperation.Kind,
                    session.LastUsageOperation.Timestamp,
                    session.LastUsageOperation.TurnNumber,
                    session.LastUsageOperation.InputTokens,
                    session.LastUsageOperation.CachedInputTokens,
                    session.LastUsageOperation.OutputTokens,
                    session.LastUsageOperation.ReasoningTokens,
                    session.LastUsageOperation.TotalTokens));

        return new DashboardSessionMetadataSnapshot(
            session.CodexInputTokens,
            session.CodexOutputTokens,
            session.CodexTotalTokens,
            session.TurnCount,
            session.SessionId,
            orchestratorSessionId,
            attempt,
            IsAttemptKnown: true,
            AvailabilityMessage: null,
            tokenUsage);
    }

    private static ImmutableList<SessionActivityEntry> EnrichActivitiesWithTokenUsage(
        ImmutableList<SessionActivityEntry> existingActivities,
        DashboardSessionMetadataSnapshot? previousMetadata,
        DashboardSessionMetadataSnapshot currentMetadata)
    {
        _ = previousMetadata;
        _ = currentMetadata;
        return existingActivities;
    }

    private static SessionActivityEntry EnrichNewActivity(
        SessionActivityEntry activity)
    {
        return SessionActivityTokenAnnotator.Normalize(activity);
    }

    private sealed record SessionState(
        SessionRecord Record,
        ImmutableList<SessionActivityEntry> Activities,
        DashboardSessionMetadataSnapshot? Metadata);
}
