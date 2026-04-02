using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Symphony.Abstractions.Orchestration;
using Symphony.Application.Runtime;
using Symphony.Host.Configuration;
using Symphony.Host.Dashboard;

namespace Symphony.Host.IntegrationTests;

public sealed class FakeDashboardDataLoaderTests
{
    [Fact]
    public async Task LoadFromStreamAsync_rejects_malformed_json()
    {
        var loader = CreateLoader();
        await using var stream = new MemoryStream("not-json"u8.ToArray());

        var result = await loader.LoadFromStreamAsync(stream, "broken.json", CreateBuiltInDataSet());

        Assert.True(result.Status.HasError);
        Assert.Contains("malformed or incompatible", result.Status.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("BASE-1", Assert.Single(result.DataSet.Sessions).Session.IssueIdentifier);
    }

    [Fact]
    public async Task LoadFromStreamAsync_merges_single_session_export_into_built_in_data()
    {
        var loader = CreateLoader();
        var envelope = new DashboardDataExportEnvelope(
            DashboardDataExportSchema.CurrentVersion,
            DateTimeOffset.UtcNow,
            DashboardDataExportSchema.SingleSessionKind,
            new DashboardDataExportSource("tests", "1.0.0", "Development"),
            new DashboardDataSessionExport(
                new SessionRecord("IMP-2", null, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow, "Succeeded", null, false),
                [
                    new SessionActivityEntry(SessionActivityKind.Outcome, DateTimeOffset.UtcNow, "Run succeeded", null),
                    new SessionActivityEntry(SessionActivityKind.DebugMessage, DateTimeOffset.UtcNow, "Received item/agentMessage/delta", "{\"method\":\"item/agentMessage/delta\"}")
                ],
                new DashboardSessionHistorySnapshot(
                    new SessionRecord("IMP-2", null, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow, "Succeeded", null, false),
                    [
                        new SessionActivityEntry(SessionActivityKind.Outcome, DateTimeOffset.UtcNow, "Run succeeded", null),
                        new SessionActivityEntry(SessionActivityKind.DebugMessage, DateTimeOffset.UtcNow, "Received item/agentMessage/delta", "{\"method\":\"item/agentMessage/delta\"}")
                    ],
                    new DashboardSessionMetadataSnapshot(
                        InputTokens: 30,
                        OutputTokens: 12,
                        TotalTokens: 42,
                        TurnCount: 3,
                        SessionId: "session-imp-2",
                        OrchestratorSessionId: "orch-imp-2",
                        Attempt: 1,
                        IsAttemptKnown: true,
                        AvailabilityMessage: "retained",
                        TokenUsage: new DashboardSessionTokenUsageSnapshot(
                            30,
                            12,
                            42,
                            28,
                            10,
                            38,
                            30,
                            12,
                            42,
                            Symphony.Domain.Sessions.SessionTokenComparisonStatus.Mismatch,
                            2,
                            2,
                            4,
                            DateTimeOffset.UtcNow.AddMinutes(-1),
                            DateTimeOffset.UtcNow))),
                IssueSnapshot: null,
                ActiveSession: null,
                RetryEntry: null,
                RecentAttempt: new DashboardRecentAttemptSnapshot("IMP-2", 1, "Succeeded", DateTimeOffset.UtcNow, 30d, null, "session-imp-2", "orch-imp-2"),
                BlockedSession: null,
                FollowUpActions: []),
            Bundle: null);
        await using var stream = new MemoryStream(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(envelope, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));

        var result = await loader.LoadFromStreamAsync(stream, "import.json", CreateBuiltInDataSet());

        Assert.False(result.Status.HasError);
        Assert.Contains(result.DataSet.Sessions, session => session.Session.IssueIdentifier == "BASE-1");
        var importedSession = Assert.Single(result.DataSet.Sessions, session => session.Session.IssueIdentifier == "IMP-2");
        Assert.Contains(importedSession.Activities, activity => activity.Kind == SessionActivityKind.DebugMessage);
        Assert.NotNull(importedSession.Metadata);
        Assert.Equal(42, importedSession.Metadata!.TokenUsage!.EffectiveTotalTokens);
    }

    [Fact]
    public async Task LoadFromStreamAsync_accepts_legacy_single_session_export_without_history_or_metadata()
    {
        var loader = CreateLoader();
        var json = """
            {
              "schemaVersion": "1.0",
              "exportedAt": "2026-04-02T09:00:00+00:00",
              "exportKind": "single-session",
              "source": {
                "application": "tests",
                "version": "1.0.0",
                "environmentName": "Development"
              },
              "singleSession": {
                "session": {
                  "issueIdentifier": "LEG-1",
                  "issueUrl": null,
                  "startedAt": "2026-04-02T08:00:00+00:00",
                  "endedAt": "2026-04-02T08:05:00+00:00",
                  "finalOutcome": "Succeeded",
                  "finalError": null,
                  "isActive": false
                },
                "activities": [
                  {
                    "kind": "debugMessage",
                    "timestamp": "2026-04-02T08:01:00+00:00",
                    "title": "Received item/agentMessage/delta",
                    "detail": "{\"method\":\"item/agentMessage/delta\"}"
                  }
                ],
                "issueSnapshot": null,
                "activeSession": null,
                "retryEntry": null,
                "recentAttempt": {
                  "issueIdentifier": "LEG-1",
                  "attempt": 1,
                  "outcome": "Succeeded",
                  "completedAt": "2026-04-02T08:05:00+00:00",
                  "durationSeconds": 300,
                  "error": null,
                  "sessionId": "legacy-thread-1",
                  "orchestratorSessionId": "orch-legacy-1"
                },
                "blockedSession": null,
                "followUpActions": []
              },
              "bundle": null
            }
            """;
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        var result = await loader.LoadFromStreamAsync(stream, "legacy-single.json", CreateBuiltInDataSet());

        Assert.False(result.Status.HasError);
        var importedSession = Assert.Single(result.DataSet.Sessions, session => session.Session.IssueIdentifier == "LEG-1");
        Assert.Single(importedSession.Activities);
    }

    [Fact]
    public async Task LoadFromStreamAsync_accepts_bundle_export_with_sessions_missing_metadata()
    {
        var loader = CreateLoader();
        var json = """
            {
              "schemaVersion": "1.0",
              "exportedAt": "2026-04-02T09:00:00+00:00",
              "exportKind": "full-bundle",
              "source": {
                "application": "tests",
                "version": "1.0.0",
                "environmentName": "Development"
              },
              "singleSession": null,
              "bundle": {
                "dashboardSnapshot": {
                  "generatedAt": "2026-04-02T09:00:00+00:00",
                  "serviceHealth": "Healthy",
                  "orchestratorMode": "Single-process in-memory",
                  "orchestratorState": "started",
                  "orchestratorStateChangedAt": "2026-04-02T08:30:00+00:00",
                  "lastPollTickAt": "2026-04-02T08:59:50+00:00",
                  "lastSuccessfulPollAt": "2026-04-02T08:59:50+00:00",
                  "lastSuccessfulPollAgeSeconds": 10,
                  "workflowLoadStatus": "Loaded",
                  "workflowLastLoadedAt": "2026-04-02T08:00:00+00:00",
                  "runningCount": 0,
                  "retryingCount": 0,
                  "inputTokens": 0,
                  "outputTokens": 0,
                  "totalTokens": 0,
                  "secondsRunning": 0,
                  "activeSessions": [],
                  "retryQueue": [],
                  "recentAttempts": [],
                  "lastError": null,
                  "workflowLastError": null,
                  "blockedCount": 0,
                  "blockedSessions": null,
                  "followUpActions": null
                },
                "runtimeState": {
                  "generatedAt": "2026-04-02T09:00:00+00:00",
                  "running": [],
                  "retrying": [],
                  "codexTotals": {
                    "inputTokens": 0,
                    "outputTokens": 0,
                    "totalTokens": 0,
                    "secondsRunning": 0
                  },
                  "rateLimits": null,
                  "blocked": null,
                  "followUpActions": null
                },
                "orchestration": {
                  "state": "started",
                  "changedAt": "2026-04-02T08:30:00+00:00"
                },
                "dashboardOptions": {
                  "debugMode": false,
                  "trackAgentMessageDeltas": false,
                  "enableFakeDataMode": true,
                  "fakeDataJsonPath": null
                },
                "sessions": [
                  {
                    "session": {
                      "issueIdentifier": "LEG-2",
                      "issueUrl": null,
                      "startedAt": "2026-04-02T08:00:00+00:00",
                      "endedAt": "2026-04-02T08:05:00+00:00",
                      "finalOutcome": "Succeeded",
                      "finalError": null,
                      "isActive": false
                    },
                    "activities": []
                  }
                ],
                "issueSnapshots": []
              }
            }
            """;
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        var result = await loader.LoadFromStreamAsync(stream, "legacy-bundle.json", CreateBuiltInDataSet());

        Assert.False(result.Status.HasError);
        var importedSession = Assert.Single(result.DataSet.Sessions);
        Assert.Equal("LEG-2", importedSession.Session.IssueIdentifier);
    }

    private static FakeDashboardDataLoader CreateLoader()
    {
        return new FakeDashboardDataLoader(
            Options.Create(new DashboardUiOptions()),
            NullLogger<FakeDashboardDataLoader>.Instance);
    }

    private static FakeDashboardDataSet CreateBuiltInDataSet()
    {
        return new FakeDashboardDataSet(
            new DashboardSnapshot(
                DateTimeOffset.UtcNow,
                "Healthy",
                "Single-process in-memory",
                OrchestratorControlState.Started,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                0d,
                "Loaded",
                DateTimeOffset.UtcNow,
                0,
                0,
                0,
                0,
                0,
                0d,
                [],
                [],
                [],
                null,
                null),
            [new DashboardSessionHistorySnapshot(new SessionRecord("BASE-1", null, DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddMinutes(-5), "Succeeded", null, false), [])],
            new Dictionary<string, OrchestratorIssueSnapshot>(StringComparer.OrdinalIgnoreCase));
    }
}
