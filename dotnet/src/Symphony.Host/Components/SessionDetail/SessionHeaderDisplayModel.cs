using Flowbite.Components;
using Symphony.Domain.Runs;

namespace Symphony.Host.Components.SessionDetail;

public sealed record SessionHeaderDisplayModel(
    string Identifier,
    string? IssueUrl,
    bool IsActive,
    string StatusText,
    RunAttemptStatus? Status,
    Badge.BadgeColor StatusColor,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt);
