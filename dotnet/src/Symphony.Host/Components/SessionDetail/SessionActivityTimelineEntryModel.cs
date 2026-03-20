using Flowbite.Components;
using Symphony.Host.Dashboard;

namespace Symphony.Host.Components.SessionDetail;

public sealed record SessionActivityTimelineEntryModel(
    SessionActivityKind Kind,
    DateTimeOffset Timestamp,
    string Title,
    string? Detail,
    TimelineColor Color);
