namespace Symphony.Host.Components.Sessions;

public sealed record SessionListRowViewModel(
    string IssueIdentifier,
    string? IssueUrl,
    bool IsActive,
    bool NeedsAttention,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    double DurationSeconds,
    string Summary,
    string? Detail);
