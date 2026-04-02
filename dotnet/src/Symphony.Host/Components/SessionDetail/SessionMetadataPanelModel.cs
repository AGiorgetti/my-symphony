using Symphony.Host.Dashboard;

namespace Symphony.Host.Components.SessionDetail;

public sealed record SessionMetadataPanelModel(
    long? InputTokens,
    long? OutputTokens,
    long? TotalTokens,
    int? TurnCount,
    string? SessionId,
    string? OrchestratorSessionId,
    int? Attempt,
    bool IsAttemptKnown,
    string? AvailabilityMessage,
    DashboardSessionTokenUsageSnapshot? TokenUsage = null);
