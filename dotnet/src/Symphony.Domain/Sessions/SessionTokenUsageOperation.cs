namespace Symphony.Domain.Sessions;

public sealed record SessionTokenUsageOperation(
    string OperationId,
    string Kind,
    DateTimeOffset Timestamp,
    int TurnNumber,
    int InputTokens,
    int CachedInputTokens,
    int OutputTokens,
    int ReasoningTokens,
    int TotalTokens);
