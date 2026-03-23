using Flowbite.Components;
using Symphony.Host.Dashboard;

namespace Symphony.Host.Components.SessionDetail;

public sealed record SessionActivityTimelineEntryModel(
    SessionActivityKind Kind,
    DateTimeOffset Timestamp,
    string TimestampLabel,
    string Title,
    string KindLabel,
    Badge.BadgeColor KindBadgeColor,
    string? Summary,
    IReadOnlyList<SessionActivityFactModel> Facts,
    string? Detail,
    string? DetailPreview,
    string? DetailToggleLabel,
    bool HasExpandableDetail,
    bool IsStructuredDetail,
    TimelineColor Color);

public sealed record SessionActivityFactModel(
    string Label,
    string Value);
