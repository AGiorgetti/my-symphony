using Symphony.Abstractions.Orchestration;
using Symphony.Application.Runtime;
using Symphony.Domain.Sessions;

namespace Symphony.Host.Dashboard;

public static class DashboardDataExportSchema
{
    public const string CurrentVersion = "1.0";
    public const string SingleSessionKind = "single-session";
    public const string FullBundleKind = "full-bundle";
}

public sealed record DashboardDataExportEnvelope(
    string SchemaVersion,
    DateTimeOffset ExportedAt,
    string ExportKind,
    DashboardDataExportSource Source,
    DashboardDataSessionExport? SingleSession,
    DashboardDataBundleExport? Bundle);

public sealed record DashboardDataExportSource(
    string Application,
    string Version,
    string EnvironmentName);

public sealed record DashboardDataSessionExport(
    SessionRecord Session,
    IReadOnlyList<SessionActivityEntry> Activities,
    DashboardSessionHistorySnapshot? History,
    OrchestratorIssueSnapshot? IssueSnapshot,
    DashboardActiveSessionSnapshot? ActiveSession,
    DashboardRetrySnapshot? RetryEntry,
    DashboardRecentAttemptSnapshot? RecentAttempt,
    DashboardBlockedSessionSnapshot? BlockedSession,
    IReadOnlyList<FollowUpActionSnapshot> FollowUpActions,
    DashboardSessionMetadataSnapshot? Metadata = null);

public sealed record DashboardDataBundleExport(
    DashboardSnapshot DashboardSnapshot,
    OrchestratorStateSnapshot RuntimeState,
    OrchestratorControlSnapshot Orchestration,
    DashboardUiOptionsSnapshot DashboardOptions,
    IReadOnlyList<DashboardSessionHistorySnapshot> Sessions,
    IReadOnlyList<OrchestratorIssueSnapshot> IssueSnapshots);

public sealed record DashboardSessionHistorySnapshot(
    SessionRecord Session,
    IReadOnlyList<SessionActivityEntry> Activities,
    DashboardSessionMetadataSnapshot? Metadata = null);

public sealed record DashboardSessionMetadataSnapshot(
    long? InputTokens,
    long? OutputTokens,
    long? TotalTokens,
    int? TurnCount,
    string? SessionId,
    string? OrchestratorSessionId,
    int? Attempt,
    bool IsAttemptKnown,
    string? AvailabilityMessage,
    DashboardSessionTokenUsageSnapshot? TokenUsage = null);

public sealed record DashboardSessionTokenUsageSnapshot(
    long EffectiveInputTokens,
    long EffectiveOutputTokens,
    long EffectiveTotalTokens,
    long ReportedInputTokens,
    long ReportedCachedInputTokens,
    long ReportedOutputTokens,
    long ReportedReasoningTokens,
    long ReportedTotalTokens,
    DateTimeOffset? LastReportedAt,
    DashboardSessionTokenOperationSnapshot? LastOperation = null);

public sealed record DashboardSessionTokenOperationSnapshot(
    string OperationId,
    string Kind,
    DateTimeOffset Timestamp,
    int TurnNumber,
    long InputTokens,
    long CachedInputTokens,
    long OutputTokens,
    long ReasoningTokens,
    long TotalTokens);

public sealed record DashboardUiOptionsSnapshot(
    bool DebugMode,
    bool TrackAgentMessageDeltas,
    bool EnableFakeDataMode,
    string? FakeDataJsonPath);

public sealed record FakeDashboardDataSet(
    DashboardSnapshot DashboardSnapshot,
    IReadOnlyList<DashboardSessionHistorySnapshot> Sessions,
    IReadOnlyDictionary<string, OrchestratorIssueSnapshot> IssueSnapshots);

public sealed record FakeDashboardImportResult(
    bool Success,
    string Message,
    FakeDashboardDataStatus Status);

public sealed record FakeDashboardDataStatus(
    bool HasImportedData,
    bool HasError,
    string Source,
    string Message);
