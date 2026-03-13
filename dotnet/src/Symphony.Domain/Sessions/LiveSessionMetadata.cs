namespace Symphony.Domain.Sessions;

public sealed record LiveSessionMetadata
{
    public LiveSessionMetadata(
        string threadId,
        string turnId,
        string? codexAppServerPid = null,
        string? lastCodexEvent = null,
        DateTimeOffset? lastCodexTimestamp = null,
        string? lastCodexMessage = null,
        int codexInputTokens = 0,
        int codexOutputTokens = 0,
        int codexTotalTokens = 0,
        int lastReportedInputTokens = 0,
        int lastReportedOutputTokens = 0,
        int lastReportedTotalTokens = 0,
        int turnCount = 0)
    {
        ThreadId = Guard.Required(threadId, nameof(threadId));
        TurnId = Guard.Required(turnId, nameof(turnId));
        SessionId = $"{ThreadId}-{TurnId}";
        CodexAppServerPid = Guard.Optional(codexAppServerPid);
        LastCodexEvent = Guard.Optional(lastCodexEvent);
        LastCodexTimestamp = lastCodexTimestamp;
        LastCodexMessage = Guard.Optional(lastCodexMessage);
        CodexInputTokens = Guard.NonNegative(codexInputTokens, nameof(codexInputTokens));
        CodexOutputTokens = Guard.NonNegative(codexOutputTokens, nameof(codexOutputTokens));
        CodexTotalTokens = Guard.NonNegative(codexTotalTokens, nameof(codexTotalTokens));
        LastReportedInputTokens = Guard.NonNegative(lastReportedInputTokens, nameof(lastReportedInputTokens));
        LastReportedOutputTokens = Guard.NonNegative(lastReportedOutputTokens, nameof(lastReportedOutputTokens));
        LastReportedTotalTokens = Guard.NonNegative(lastReportedTotalTokens, nameof(lastReportedTotalTokens));
        TurnCount = Guard.NonNegative(turnCount, nameof(turnCount));

        if (CodexTotalTokens < CodexInputTokens + CodexOutputTokens)
        {
            throw new ArgumentOutOfRangeException(
                nameof(codexTotalTokens),
                codexTotalTokens,
                "Total tokens cannot be smaller than the sum of input and output tokens.");
        }

        if (LastReportedTotalTokens < LastReportedInputTokens + LastReportedOutputTokens)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastReportedTotalTokens),
                lastReportedTotalTokens,
                "Reported total tokens cannot be smaller than the sum of reported input and output tokens.");
        }
    }

    public string SessionId { get; }

    public string ThreadId { get; }

    public string TurnId { get; }

    public string? CodexAppServerPid { get; }

    public string? LastCodexEvent { get; }

    public DateTimeOffset? LastCodexTimestamp { get; }

    public string? LastCodexMessage { get; }

    public int CodexInputTokens { get; }

    public int CodexOutputTokens { get; }

    public int CodexTotalTokens { get; }

    public int LastReportedInputTokens { get; }

    public int LastReportedOutputTokens { get; }

    public int LastReportedTotalTokens { get; }

    public int TurnCount { get; }
}
