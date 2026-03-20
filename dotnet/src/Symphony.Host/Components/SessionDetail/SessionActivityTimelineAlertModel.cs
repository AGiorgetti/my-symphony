using Flowbite.Components;

namespace Symphony.Host.Components.SessionDetail;

public sealed record SessionActivityTimelineAlertModel(
    AlertColor Color,
    string Emphasis,
    string Message);
