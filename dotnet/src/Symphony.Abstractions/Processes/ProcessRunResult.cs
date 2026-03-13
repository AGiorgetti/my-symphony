namespace Symphony.Abstractions.Processes;

public sealed record ProcessRunResult
{
    public ProcessRunResult(
        int exitCode,
        string? standardOutput,
        string? standardError,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt)
    {
        if (finishedAt < startedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(finishedAt),
                finishedAt,
                "FinishedAt cannot be earlier than StartedAt.");
        }

        ExitCode = exitCode;
        StandardOutput = standardOutput ?? string.Empty;
        StandardError = standardError ?? string.Empty;
        StartedAt = startedAt;
        FinishedAt = finishedAt;
    }

    public int ExitCode { get; }

    public string StandardOutput { get; }

    public string StandardError { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset FinishedAt { get; }

    public TimeSpan Duration => FinishedAt - StartedAt;
}
