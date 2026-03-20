namespace Symphony.Host.Components.SessionDetail;

public sealed record SessionMetadataPanelModel(
    long? InputTokens,
    long? OutputTokens,
    long? TotalTokens,
    int? TurnCount,
    string? SessionId,
    int? Attempt,
    bool IsAttemptKnown,
    string? AvailabilityMessage);
