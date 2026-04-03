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
                        Activities = existing.Activities.Add(EnrichNewActivity(activity, existing.Metadata))
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
                            issueIdentifier,
                            ImmutableList<SessionActivityEntry>.Empty,
                            previousMetadata: null,
                            metadata,
                            timestamp);
                        return new SessionState(
                            new SessionRecord(issueIdentifier, null, timestamp, null, null, null, true),
                            activities,
                            metadata);
                    },
                    (_, existing) =>
                    {
                        var metadata = BuildMetadataSnapshot(session, attempt, orchestratorSessionId);
                        var activities = EnrichActivitiesWithTokenUsage(
                            issueIdentifier,
                            existing.Activities,
                            existing.Metadata,
                            metadata,
                            timestamp);
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
            session.EstimatedInputTokens,
            session.EstimatedOutputTokens,
            session.EstimatedTotalTokens,
            session.LastReportedInputTokens,
            session.LastReportedCachedInputTokens,
            session.LastReportedOutputTokens,
            session.LastReportedReasoningTokens,
            session.LastReportedTotalTokens,
            session.TokenComparisonStatus,
            session.TokenInputDelta,
            session.TokenOutputDelta,
            session.TokenTotalDelta,
            session.LastEstimatedTokenAt,
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
        string issueIdentifier,
        ImmutableList<SessionActivityEntry> existingActivities,
        DashboardSessionMetadataSnapshot? previousMetadata,
        DashboardSessionMetadataSnapshot currentMetadata,
        DateTimeOffset timestamp)
    {
        var updatedActivities = existingActivities;
        var previousUsage = previousMetadata?.TokenUsage;
        var currentUsage = currentMetadata.TokenUsage;
        if (currentUsage is null)
        {
            return updatedActivities;
        }

        if (previousUsage is null
            || previousUsage.EstimatedInputTokens != currentUsage.EstimatedInputTokens
            || previousUsage.EstimatedOutputTokens != currentUsage.EstimatedOutputTokens
            || previousUsage.EstimatedTotalTokens != currentUsage.EstimatedTotalTokens)
        {
            updatedActivities = AttachTokenUsageToNearestActivity(
                updatedActivities,
                currentUsage.LastEstimatedAt ?? timestamp,
                CreateActivityTokenSnapshot("estimated", currentUsage),
                static entry => entry.Title.StartsWith("Sent turn/start", StringComparison.Ordinal)
                    || entry.Title.StartsWith("Received response", StringComparison.Ordinal)
                    || entry.Title.StartsWith("Turn started", StringComparison.OrdinalIgnoreCase));
        }

        if (previousUsage is null
            || previousUsage.ReportedInputTokens != currentUsage.ReportedInputTokens
            || previousUsage.ReportedOutputTokens != currentUsage.ReportedOutputTokens
            || previousUsage.ReportedTotalTokens != currentUsage.ReportedTotalTokens)
        {
            updatedActivities = AttachTokenUsageToNearestActivity(
                updatedActivities,
                currentUsage.LastReportedAt ?? timestamp,
                CreateActivityTokenSnapshot("reported", currentUsage),
                static entry => entry.Title.StartsWith("Received thread/tokenUsage/updated", StringComparison.Ordinal)
                    || entry.Title.StartsWith("Received turn/completed", StringComparison.Ordinal)
                    || entry.Title.StartsWith("Received turn/failed", StringComparison.Ordinal)
                    || entry.Title.StartsWith("Received turn/cancelled", StringComparison.Ordinal));
        }

        if (currentUsage.LastOperation is not null
            && previousUsage?.LastOperation?.OperationId != currentUsage.LastOperation.OperationId)
        {
            updatedActivities = AttachTokenUsageToNearestActivity(
                updatedActivities,
                currentUsage.LastOperation.Timestamp,
                CreateActivityTokenSnapshot("operation", currentUsage),
                static entry => entry.Title.StartsWith("Received thread/tokenUsage/updated", StringComparison.Ordinal)
                    || entry.Title.StartsWith("Received turn/completed", StringComparison.Ordinal)
                    || entry.Title.StartsWith("Received turn/failed", StringComparison.Ordinal)
                    || entry.Title.StartsWith("Received turn/cancelled", StringComparison.Ordinal));
        }

        if (currentUsage.ComparisonStatus == SessionTokenComparisonStatus.Mismatch
            && previousUsage?.ComparisonStatus != SessionTokenComparisonStatus.Mismatch)
        {
            updatedActivities = AttachTokenUsageToNearestActivity(
                updatedActivities,
                currentUsage.LastReportedAt ?? currentUsage.LastEstimatedAt ?? timestamp,
                CreateActivityTokenSnapshot("comparison", currentUsage),
                static entry => entry.Kind is SessionActivityKind.Warning or SessionActivityKind.Error
                    || entry.Title.StartsWith("Received thread/tokenUsage/updated", StringComparison.Ordinal)
                    || entry.Title.StartsWith("Received turn/completed", StringComparison.Ordinal)
                    || entry.Title.StartsWith("Received turn/failed", StringComparison.Ordinal)
                    || entry.Title.StartsWith("Received turn/cancelled", StringComparison.Ordinal));
        }

        return updatedActivities;
    }

    private static ImmutableList<SessionActivityEntry> AttachTokenUsageToNearestActivity(
        ImmutableList<SessionActivityEntry> activities,
        DateTimeOffset targetTimestamp,
        SessionActivityTokenSnapshot tokenUsage,
        Func<SessionActivityEntry, bool> preferredPredicate)
    {
        if (activities.Count == 0)
        {
            return activities;
        }

        var preferredIndex = -1;
        for (var index = activities.Count - 1; index >= 0; index--)
        {
            var activity = activities[index];
            if (activity.Timestamp > targetTimestamp)
            {
                continue;
            }

            if (preferredPredicate(activity))
            {
                preferredIndex = index;
                break;
            }
        }

        var fallbackIndex = preferredIndex >= 0
            ? preferredIndex
            : activities.FindLastIndex(activity => activity.Timestamp <= targetTimestamp);

        if (fallbackIndex < 0)
        {
            fallbackIndex = activities.Count - 1;
        }

        var existing = activities[fallbackIndex];
        return activities.SetItem(
            fallbackIndex,
            existing with
            {
                TokenUsage = MergeTokenUsage(existing.TokenUsage, tokenUsage)
            });
    }

    private static SessionActivityEntry EnrichNewActivity(
        SessionActivityEntry activity,
        DashboardSessionMetadataSnapshot? metadata)
    {
        if (metadata?.TokenUsage is null || activity.TokenUsage is not null)
        {
            return activity;
        }

        if (activity.Kind == SessionActivityKind.LifecycleMilestone)
        {
            return activity;
        }

        return activity with
        {
            TokenUsage = CreateActivityTokenSnapshot("current", metadata.TokenUsage)
        };
    }

    private static SessionActivityTokenSnapshot MergeTokenUsage(
        SessionActivityTokenSnapshot? existing,
        SessionActivityTokenSnapshot incoming)
    {
        if (existing is null)
        {
            return incoming;
        }

        var preferredSource = GetSourcePriority(existing.Source) >= GetSourcePriority(incoming.Source)
            ? existing.Source
            : incoming.Source;

        return incoming with
        {
            Source = preferredSource
        };
    }

    private static int GetSourcePriority(string source)
    {
        return source switch
        {
            "operation" => 4,
            "reported" => 3,
            "comparison" => 2,
            "estimated" => 1,
            _ => 0
        };
    }

    private static SessionActivityTokenSnapshot CreateActivityTokenSnapshot(
        string source,
        DashboardSessionTokenUsageSnapshot tokenUsage)
    {
        return new SessionActivityTokenSnapshot(
            source,
            tokenUsage.EffectiveInputTokens,
            tokenUsage.EffectiveOutputTokens,
            tokenUsage.EffectiveTotalTokens,
            tokenUsage.EstimatedInputTokens,
            tokenUsage.EstimatedOutputTokens,
            tokenUsage.EstimatedTotalTokens,
            tokenUsage.ReportedInputTokens,
            tokenUsage.ReportedCachedInputTokens,
            tokenUsage.ReportedOutputTokens,
            tokenUsage.ReportedReasoningTokens,
            tokenUsage.ReportedTotalTokens,
            tokenUsage.ComparisonStatus,
            tokenUsage.InputDelta,
            tokenUsage.OutputDelta,
            tokenUsage.TotalDelta,
            tokenUsage.LastEstimatedAt,
            tokenUsage.LastReportedAt,
            tokenUsage.LastOperation);
    }

    private sealed record SessionState(
        SessionRecord Record,
        ImmutableList<SessionActivityEntry> Activities,
        DashboardSessionMetadataSnapshot? Metadata);
}
