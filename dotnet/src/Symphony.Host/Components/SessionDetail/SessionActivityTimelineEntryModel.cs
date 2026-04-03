using MudBlazor;
using Symphony.Host.Dashboard;

namespace Symphony.Host.Components.SessionDetail;

public sealed record SessionActivityTimelineEntryModel(
    SessionActivityKind Kind,
    DateTimeOffset Timestamp,
    string TimestampLabel,
    string Title,
    string KindLabel,
    Color KindBadgeColor,
    string? Summary,
    IReadOnlyList<SessionActivityFactModel> Facts,
    string? Detail,
    string? DetailPreview,
    string? DetailToggleLabel,
    bool HasExpandableDetail,
    bool IsStructuredDetail,
    Color Color,
    bool IsEmptyAgentMessage = false,
    bool HasTokenUsage = false,
    string? TokenSourceLabel = null,
    string CompactTimestampLabel = "",
    string? CompactPreview = null,
    string? MethodTag = null);

public sealed record SessionActivityFactModel(
    string Label,
    string Value);
