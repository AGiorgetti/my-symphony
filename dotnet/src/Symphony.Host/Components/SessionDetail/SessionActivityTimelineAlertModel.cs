using MudBlazor;

namespace Symphony.Host.Components.SessionDetail;

public sealed record SessionActivityTimelineAlertModel(
    Severity Color,
    string Emphasis,
    string Message);
