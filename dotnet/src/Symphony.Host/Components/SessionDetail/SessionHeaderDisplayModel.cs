using MudBlazor;
using Symphony.Domain.Runs;

namespace Symphony.Host.Components.SessionDetail;

public sealed record SessionHeaderDisplayModel(
    string Identifier,
    string? IssueUrl,
    bool IsActive,
    string StatusText,
    RunAttemptStatus? Status,
    Color StatusColor,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt);
