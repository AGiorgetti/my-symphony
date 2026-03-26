using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Symphony.Application.Orchestration;
using Symphony.Host.Configuration;

namespace Symphony.Host.Dashboard;

public sealed class SessionActivityStore(
    ILogger<SessionActivityStore> logger,
    IOptions<DashboardUiOptions>? dashboardUiOptions = null) : ISessionActivityStore, IAgentDebugTranscriptSink
{
    private const int ActivityHistoryLimit = 500;

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
                        ImmutableList<SessionActivityEntry>.Empty),
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
                        TrimActivities(ImmutableList<SessionActivityEntry>.Empty.Add(activity), activity.Timestamp)),
                    (_, existing) => existing with
                    {
                        Activities = TrimActivities(existing.Activities.Add(activity), activity.Timestamp)
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
                        ImmutableList<SessionActivityEntry>.Empty),
                    (_, existing) => existing with
                    {
                        Record = existing.Record with
                        {
                            EndedAt = endedAt,
                            FinalOutcome = outcome,
                            FinalError = error,
                            IsActive = false
                        }
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

    private static ImmutableList<SessionActivityEntry> TrimActivities(
        ImmutableList<SessionActivityEntry> activities,
        DateTimeOffset timestamp)
    {
        if (activities.Count <= ActivityHistoryLimit)
        {
            return activities;
        }

        return activities.GetRange(activities.Count - 499, 499)
            .Add(
                new SessionActivityEntry(
                    SessionActivityKind.Warning,
                    timestamp,
                    "Activity history trimmed",
                    "Older session activity entries were removed to keep the timeline bounded."));
    }

    private sealed record SessionState(SessionRecord Record, ImmutableList<SessionActivityEntry> Activities);
}
