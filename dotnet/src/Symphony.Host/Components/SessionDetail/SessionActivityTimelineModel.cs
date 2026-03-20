namespace Symphony.Host.Components.SessionDetail;

public sealed record SessionActivityTimelineModel(
    IReadOnlyList<SessionActivityTimelineEntryModel> Entries,
    SessionActivityTimelineAlertModel? LatestAttentionAlert,
    SessionActivityTimelineAlertModel? FailureAlert);
