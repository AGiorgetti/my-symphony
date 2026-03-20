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
    string? Detail,
    string? DetailPreview,
    bool HasExpandableDetail,
    bool IsStructuredDetail,
    TimelineColor Color);
