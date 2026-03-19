namespace Symphony.Host.Dashboard;

public interface ISessionActivityStore
{
    void RecordSessionStart(string issueIdentifier, DateTimeOffset startedAt, string? issueUrl = null);

    void RecordActivity(string issueIdentifier, SessionActivityEntry activity);

    void RecordSessionEnd(string issueIdentifier, DateTimeOffset endedAt, string outcome, string? error = null);

    IReadOnlyList<SessionRecord> GetAllSessions();

    IReadOnlyList<SessionRecord> GetActiveSessions();

    IReadOnlyList<SessionRecord> GetEndedSessions();

    SessionRecord? GetSession(string issueIdentifier);

    IReadOnlyList<SessionActivityEntry> GetActivities(string issueIdentifier);
}
