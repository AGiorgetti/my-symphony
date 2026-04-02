using System.Text.Json;
using Microsoft.Extensions.Logging;
using Symphony.Abstractions.Orchestration;
using Symphony.Application.Orchestration;
using Symphony.Application.Runtime;
using Symphony.Domain.Sessions;

namespace Symphony.Host.Dashboard;

public sealed class FakeDashboardPageDataSource
{
    private readonly object _sync = new();
    private readonly IFakeDashboardDataLoader _dataLoader;
    private readonly FakeDashboardDataSet _builtInDataSet;
    private FakeDashboardDataSet _dataSet;
    private FakeDashboardDataStatus _status;

    public FakeDashboardPageDataSource(ILoggerFactory loggerFactory, IFakeDashboardDataLoader dataLoader)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(dataLoader);

        _dataLoader = dataLoader;
        _builtInDataSet = CreateFixtureDataSet(loggerFactory);
        var loaded = _dataLoader.LoadConfigured(_builtInDataSet);
        _dataSet = loaded.DataSet;
        _status = loaded.Status;
    }

    public FakeDashboardDataStatus GetStatus()
    {
        lock (_sync)
        {
            return _status;
        }
    }

    public Task<DashboardSnapshot> GetDashboardSnapshotAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(_dataSet.DashboardSnapshot);
        }
    }

    public IReadOnlyList<SessionRecord> GetAllSessions()
    {
        lock (_sync)
        {
            return _dataSet.Sessions.Select(history => history.Session).OrderByDescending(record => record.StartedAt).ToArray();
        }
    }

    public SessionRecord? GetSession(string issueIdentifier)
    {
        lock (_sync)
        {
            return _dataSet.Sessions
                .Select(history => history.Session)
                .FirstOrDefault(session => string.Equals(session.IssueIdentifier, issueIdentifier, StringComparison.OrdinalIgnoreCase));
        }
    }

    public DashboardSessionHistorySnapshot? GetSessionHistory(string issueIdentifier)
    {
        lock (_sync)
        {
            return _dataSet.Sessions
                .FirstOrDefault(history => string.Equals(history.Session.IssueIdentifier, issueIdentifier, StringComparison.OrdinalIgnoreCase));
        }
    }

    public IReadOnlyList<SessionActivityEntry> GetActivities(string issueIdentifier)
    {
        lock (_sync)
        {
            return _dataSet.Sessions
                .FirstOrDefault(history => string.Equals(history.Session.IssueIdentifier, issueIdentifier, StringComparison.OrdinalIgnoreCase))
                ?.Activities
                ?? [];
        }
    }

    public Task<OrchestratorIssueSnapshot?> GetIssueSnapshotAsync(
        string issueIdentifier,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(
                _dataSet.IssueSnapshots.TryGetValue(issueIdentifier, out var snapshot)
                    ? snapshot
                    : null);
        }
    }

    public async Task<FakeDashboardImportResult> ImportAsync(
        Stream jsonStream,
        string? sourceName,
        CancellationToken cancellationToken = default)
    {
        var loaded = await _dataLoader.LoadFromStreamAsync(jsonStream, sourceName, _builtInDataSet, cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            _dataSet = loaded.DataSet;
            _status = loaded.Status;
            return new FakeDashboardImportResult(!loaded.Status.HasError, loaded.Status.Message, _status);
        }
    }

    public Task<FollowUpActionResolutionResult> ResolveFollowUpActionAsync(
        FollowUpActionResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_dataSet.IssueSnapshots.TryGetValue(request.IssueIdentifier, out var issueSnapshot))
            {
                return Task.FromResult(
                    new FollowUpActionResolutionResult(
                        FollowUpActionResolutionStatus.BlockedIssueNotFound,
                        Requeued: false,
                        Action: null,
                        Message: $"Issue '{request.IssueIdentifier}' is not present in the fake dataset."));
            }

            var pendingAction = (issueSnapshot.FollowUpActions ?? Array.Empty<FollowUpActionSnapshot>())
                .FirstOrDefault(
                    action => string.Equals(action.FollowUpActionId, request.FollowUpActionId, StringComparison.OrdinalIgnoreCase)
                        && action.Status == FollowUpActionStatus.Pending);

            if (pendingAction is null)
            {
                return Task.FromResult(
                    new FollowUpActionResolutionResult(
                        FollowUpActionResolutionStatus.ActionNotFound,
                        Requeued: false,
                        Action: null,
                        Message: $"Follow-up action '{request.FollowUpActionId}' is not pending in the fake dataset."));
            }

            var resolvedAt = new DateTimeOffset(2026, 4, 1, 9, 10, 0, TimeSpan.Zero);
            var selectedOptionId = request.SelectedOptionId ?? pendingAction.Options.FirstOrDefault()?.OptionId ?? "resume";
            var resolvedAction = pendingAction with
            {
                Status = FollowUpActionStatus.Resolved,
                ResolvedBy = request.ResolvedBy,
                ResolvedAt = resolvedAt,
                SelectedOptionId = selectedOptionId,
                Notes = request.Notes
            };
            var resumedRunningSnapshot = new RunningIssueSnapshot(
                issueSnapshot.IssueId,
                issueSnapshot.IssueIdentifier,
                "StreamingTurn",
                "fake-thread-303-turn-4",
                4,
                "turn_resumed",
                "Resumed after manual review.",
                new DateTimeOffset(2026, 4, 1, 9, 10, 0, TimeSpan.Zero),
                resolvedAt,
                410,
                152,
                562,
                issueSnapshot.OrchestratorSessionId);
            var resumedIssue = issueSnapshot with
            {
                Status = "running",
                Running = resumedRunningSnapshot,
                Blocked = null,
                LastError = null,
                RecentEvents =
                [
                    new RuntimeEventSnapshot(resolvedAt.AddSeconds(-10), "blocked_error", "Waiting for manual review."),
                    new RuntimeEventSnapshot(resolvedAt, "follow_up_resolved", "Fake mode resumed the session after operator review.")
                ],
                FollowUpActions =
                [
                    resolvedAction,
                    .. (issueSnapshot.FollowUpActions ?? [])
                        .Where(action => !string.Equals(action.FollowUpActionId, resolvedAction.FollowUpActionId, StringComparison.OrdinalIgnoreCase))
                ]
            };

            var histories = _dataSet.Sessions
                .Where(history => !string.Equals(history.Session.IssueIdentifier, issueSnapshot.IssueIdentifier, StringComparison.OrdinalIgnoreCase))
                .Append(
                    UpdateSessionHistory(
                        issueSnapshot.IssueIdentifier,
                        resumedRunningSnapshot.StartedAt,
                        issueSnapshot.IssueIdentifier,
                        selectedOptionId,
                        resolvedAt))
                .OrderByDescending(history => history.Session.StartedAt)
                .ToArray();

            var issueSnapshots = new Dictionary<string, OrchestratorIssueSnapshot>(_dataSet.IssueSnapshots, StringComparer.OrdinalIgnoreCase)
            {
                [issueSnapshot.IssueIdentifier] = resumedIssue
            };

            var existingActiveSessions = _dataSet.DashboardSnapshot.ActiveSessions
                .Where(candidate => !string.Equals(candidate.IssueIdentifier, issueSnapshot.IssueIdentifier, StringComparison.OrdinalIgnoreCase))
                .ToList();
            existingActiveSessions.Add(
                new DashboardActiveSessionSnapshot(
                    issueSnapshot.IssueIdentifier,
                    "StreamingTurn",
                    resumedRunningSnapshot.SessionId,
                    resumedRunningSnapshot.TurnCount,
                    resumedRunningSnapshot.LastEvent,
                    resumedRunningSnapshot.LastMessage,
                    resumedRunningSnapshot.StartedAt,
                    resumedRunningSnapshot.LastEventAt,
                    resumedRunningSnapshot.TotalTokens));

            var blockedSessions = (_dataSet.DashboardSnapshot.BlockedSessions ?? [])
                .Where(candidate => !string.Equals(candidate.IssueIdentifier, issueSnapshot.IssueIdentifier, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var followUpActions = (_dataSet.DashboardSnapshot.FollowUpActions ?? [])
                .Where(action => !string.Equals(action.FollowUpActionId, resolvedAction.FollowUpActionId, StringComparison.OrdinalIgnoreCase))
                .Prepend(resolvedAction)
                .ToArray();
            var recentAttempts = _dataSet.DashboardSnapshot.RecentAttempts
                .Prepend(
                    new DashboardRecentAttemptSnapshot(
                        issueSnapshot.IssueIdentifier,
                        resumedIssue.CurrentRetryAttempt,
                        "Resumed",
                        resolvedAt,
                        18d,
                        null,
                        resumedRunningSnapshot.SessionId,
                        resumedIssue.OrchestratorSessionId))
                .ToArray();

            _dataSet = new FakeDashboardDataSet(
                _dataSet.DashboardSnapshot with
                {
                    GeneratedAt = resolvedAt,
                    RunningCount = existingActiveSessions.Count,
                    BlockedCount = blockedSessions.Length,
                    ActiveSessions = existingActiveSessions.OrderByDescending(session => session.StartedAt).ToArray(),
                    BlockedSessions = blockedSessions,
                    FollowUpActions = followUpActions,
                    RecentAttempts = recentAttempts
                },
                histories,
                issueSnapshots);
            _status = _status with { Message = "Fake mode data updated after follow-up resolution." };

            return Task.FromResult(
                new FollowUpActionResolutionResult(
                    FollowUpActionResolutionStatus.Resolved,
                    Requeued: true,
                    Action: resolvedAction,
                    Message: "Fake mode resumed the blocked session."));
        }
    }

    private static DashboardSessionHistorySnapshot UpdateSessionHistory(
        string issueIdentifier,
        DateTimeOffset startedAt,
        string displayIdentifier,
        string selectedOptionId,
        DateTimeOffset resolvedAt)
    {
        var session = new SessionRecord(
            issueIdentifier,
            $"https://example.invalid/issues/{displayIdentifier}",
            startedAt,
            EndedAt: null,
            FinalOutcome: null,
            FinalError: null,
            IsActive: true);
        return new DashboardSessionHistorySnapshot(
            session,
            [
                new SessionActivityEntry(
                    SessionActivityKind.LifecycleMilestone,
                    resolvedAt,
                    "Follow-up action resolved",
                    $"Operator selected '{selectedOptionId}' in fake mode."),
                new SessionActivityEntry(
                    SessionActivityKind.AgentMessage,
                    resolvedAt.AddSeconds(5),
                    "Session resumed",
                    "Fake mode resumed the blocked session for UI validation.")
            ],
            new DashboardSessionMetadataSnapshot(
                InputTokens: 410,
                OutputTokens: 152,
                TotalTokens: 562,
                TurnCount: 4,
                SessionId: "fake-thread-303-turn-4",
                OrchestratorSessionId: "orch-fake-303",
                Attempt: 2,
                IsAttemptKnown: true,
                AvailabilityMessage: null,
                TokenUsage: new DashboardSessionTokenUsageSnapshot(
                    410,
                    152,
                    562,
                    388,
                    150,
                    538,
                    410,
                    152,
                    562,
                    SessionTokenComparisonStatus.Mismatch,
                    22,
                    2,
                    24,
                    resolvedAt.AddSeconds(-10),
                    resolvedAt)));
    }

    private static FakeDashboardDataSet CreateFixtureDataSet(ILoggerFactory loggerFactory)
    {
        var sessionActivityStore = new SessionActivityStore(loggerFactory.CreateLogger<SessionActivityStore>());
        var generatedAt = new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero);
        SeedActiveSession(sessionActivityStore);
        SeedRetryingSession(sessionActivityStore);
        SeedBlockedSession(sessionActivityStore);
        SeedFailedDebugSession(sessionActivityStore);
        SeedSucceededSession(sessionActivityStore);

        var blockedAction = new FollowUpActionSnapshot(
            "fai-303",
            "303",
            "ABC-303",
            "orch-fake-303",
            generatedAt.AddMinutes(-8),
            BlockingReasonCode.ManualDecisionRequired,
            "Deployment target requires a manual approval step.",
            "Review the staged deployment plan, then resolve the follow-up action to resume the session.",
            [new FollowUpActionOptionSnapshot("resume", "Resume", "Resume after the review is complete.")],
            FollowUpActionStatus.Pending,
            ResolvedBy: null,
            ResolvedAt: null,
            SelectedOptionId: null,
            Notes: null);

        var dashboardSnapshot = new DashboardSnapshot(
            generatedAt,
            "Healthy",
            "Single-process in-memory",
            OrchestratorControlState.Started,
            generatedAt,
            generatedAt,
            generatedAt.AddSeconds(-25),
            25d,
            "Loaded",
            generatedAt.AddMinutes(-15),
            RunningCount: 1,
            RetryingCount: 1,
            InputTokens: 1280,
            OutputTokens: 420,
            TotalTokens: 1700,
            SecondsRunning: 1480d,
            ActiveSessions:
            [
                new DashboardActiveSessionSnapshot(
                    "ABC-101",
                    "StreamingTurn",
                    "fake-thread-101-turn-7",
                    7,
                    "turn_completed",
                    "Refined the dashboard filters and queued one follow-up.",
                    generatedAt.AddMinutes(-22),
                    generatedAt.AddSeconds(-12),
                    784)
            ],
            RetryQueue:
            [
                new DashboardRetrySnapshot(
                    "ABC-202",
                    3,
                    generatedAt.AddMinutes(4),
                    "Rate limit exceeded on the last attempt.")
            ],
            RecentAttempts:
            [
                new DashboardRecentAttemptSnapshot(
                    "ABC-404",
                    1,
                    "Failed",
                    generatedAt.AddMinutes(-4),
                    192d,
                    "Prompt build failed after loading a large diagnostics payload.",
                    "fake-thread-404-turn-3",
                    "orch-fake-404"),
                new DashboardRecentAttemptSnapshot(
                    "ABC-505",
                    2,
                    "Succeeded",
                    generatedAt.AddMinutes(-11),
                    260d,
                    null,
                    "fake-thread-505-turn-5",
                    "orch-fake-505")
            ],
            LastError: null,
            WorkflowLastError: null,
            BlockedCount: 1,
            BlockedSessions:
            [
                new DashboardBlockedSessionSnapshot(
                    "ABC-303",
                    "orch-fake-303",
                    blockedAction.FollowUpActionId,
                    generatedAt.AddMinutes(-8),
                    blockedAction.ErrorMessage,
                    blockedAction.RequiredUserAction)
            ],
            FollowUpActions: [blockedAction]);

        var issueSnapshots = new Dictionary<string, OrchestratorIssueSnapshot>(StringComparer.OrdinalIgnoreCase)
        {
            ["ABC-101"] = new OrchestratorIssueSnapshot(
                "ABC-101",
                "101",
                "running",
                RestartCount: 1,
                CurrentRetryAttempt: 1,
                Running: new RunningIssueSnapshot(
                    "101",
                    "ABC-101",
                    "StreamingTurn",
                    "fake-thread-101-turn-7",
                    7,
                    "turn_completed",
                    "Refined the dashboard filters and queued one follow-up.",
                    generatedAt.AddMinutes(-22),
                    generatedAt.AddSeconds(-12),
                    520,
                    264,
                    784,
                    "orch-fake-101"),
                Retry: null,
                LastError: null,
                RecentEvents:
                [
                    new RuntimeEventSnapshot(generatedAt.AddMinutes(-20), "turn_started", "Loaded fake dashboard scenario."),
                    new RuntimeEventSnapshot(generatedAt.AddSeconds(-12), "turn_completed", "Refined the dashboard filters and queued one follow-up.")
                ],
                OrchestratorSessionId: "orch-fake-101"),
            ["ABC-202"] = new OrchestratorIssueSnapshot(
                "ABC-202",
                "202",
                "retrying",
                RestartCount: 2,
                CurrentRetryAttempt: 3,
                Running: null,
                Retry: new RetryDispatchSnapshot(
                    "202",
                    "ABC-202",
                    3,
                    generatedAt.AddMinutes(4),
                    "Rate limit exceeded on the last attempt."),
                LastError: "Rate limit exceeded on the last attempt.",
                RecentEvents:
                [
                    new RuntimeEventSnapshot(generatedAt.AddMinutes(-3), "retry_scheduled", "Waiting for the retry window.")
                ],
                OrchestratorSessionId: "orch-fake-202"),
            ["ABC-303"] = new OrchestratorIssueSnapshot(
                "ABC-303",
                "303",
                "blocked_error",
                RestartCount: 1,
                CurrentRetryAttempt: 2,
                Running: null,
                Retry: null,
                LastError: blockedAction.ErrorMessage,
                RecentEvents:
                [
                    new RuntimeEventSnapshot(generatedAt.AddMinutes(-9), "manual_decision_required", "Agent requested a manual approval."),
                    new RuntimeEventSnapshot(generatedAt.AddMinutes(-8), "blocked_error", blockedAction.ErrorMessage)
                ],
                OrchestratorSessionId: "orch-fake-303",
                Blocked: new BlockedDispatchSnapshot(
                    "303",
                    "ABC-303",
                    "orch-fake-303",
                    2,
                    generatedAt.AddMinutes(-8),
                    BlockingReasonCode.ManualDecisionRequired,
                    blockedAction.ErrorMessage,
                    blockedAction.RequiredUserAction,
                    blockedAction.FollowUpActionId),
                FollowUpActions: [blockedAction]),
            ["ABC-404"] = new OrchestratorIssueSnapshot(
                "ABC-404",
                "404",
                "failed",
                RestartCount: 1,
                CurrentRetryAttempt: 1,
                Running: null,
                Retry: null,
                LastError: "Prompt build failed after loading a large diagnostics payload.",
                RecentEvents:
                [
                    new RuntimeEventSnapshot(generatedAt.AddMinutes(-5), "workspace_loaded", "Captured a large diagnostics snapshot."),
                    new RuntimeEventSnapshot(generatedAt.AddMinutes(-4), "failed", "Prompt build failed after loading a large diagnostics payload.")
                ],
                OrchestratorSessionId: "orch-fake-404"),
            ["ABC-505"] = new OrchestratorIssueSnapshot(
                "ABC-505",
                "505",
                "succeeded",
                RestartCount: 2,
                CurrentRetryAttempt: 2,
                Running: null,
                Retry: null,
                LastError: null,
                RecentEvents:
                [
                    new RuntimeEventSnapshot(generatedAt.AddMinutes(-11), "completed", "Published a clean success result.")
                ],
                OrchestratorSessionId: "orch-fake-505")
        };

        return new FakeDashboardDataSet(
            dashboardSnapshot,
            sessionActivityStore.GetAllSessionHistories(),
            issueSnapshots);
    }

    private static void SeedActiveSession(SessionActivityStore store)
    {
        var startedAt = new DateTimeOffset(2026, 4, 1, 8, 38, 0, TimeSpan.Zero);
        store.RecordSessionStart("ABC-101", startedAt, "https://example.invalid/issues/ABC-101");
        store.RecordSessionMetadata(
            "ABC-101",
            startedAt.AddMinutes(21),
            new LiveSessionMetadata(
                "fake-thread-101",
                "turn-7",
                lastCodexEvent: "turn_completed",
                lastCodexTimestamp: startedAt.AddMinutes(21),
                lastCodexMessage: "Refined the dashboard filters and queued one follow-up.",
                codexInputTokens: 520,
                codexOutputTokens: 264,
                codexTotalTokens: 784,
                estimatedInputTokens: 500,
                estimatedOutputTokens: 252,
                estimatedTotalTokens: 752,
                lastReportedInputTokens: 520,
                lastReportedOutputTokens: 264,
                lastReportedTotalTokens: 784,
                tokenComparisonStatus: SessionTokenComparisonStatus.Mismatch,
                tokenInputDelta: 20,
                tokenOutputDelta: 12,
                tokenTotalDelta: 32,
                lastEstimatedTokenAt: startedAt.AddMinutes(20),
                lastReportedTokenAt: startedAt.AddMinutes(21),
                turnCount: 7),
            attempt: 1,
            orchestratorSessionId: "orch-fake-101");
        store.RecordActivity("ABC-101", new SessionActivityEntry(SessionActivityKind.LifecycleMilestone, startedAt, "Session started", "Fake mode bootstrapped an active dashboard scenario."));
        store.RecordActivity("ABC-101", new SessionActivityEntry(SessionActivityKind.ProgressUpdate, startedAt.AddMinutes(8), "Workspace hydrated", "Loaded design notes and previous attempts."));
        store.RecordActivity("ABC-101", new SessionActivityEntry(SessionActivityKind.Warning, startedAt.AddMinutes(14), "Minor warning", "A non-blocking lint warning is waiting for cleanup."));
        store.RecordActivity("ABC-101", new SessionActivityEntry(SessionActivityKind.AgentMessage, startedAt.AddMinutes(21), "turn_completed", "Refined the dashboard filters and queued one follow-up."));
    }

    private static void SeedRetryingSession(SessionActivityStore store)
    {
        var startedAt = new DateTimeOffset(2026, 4, 1, 8, 49, 0, TimeSpan.Zero);
        store.RecordSessionStart("ABC-202", startedAt, "https://example.invalid/issues/ABC-202");
        store.RecordSessionMetadata(
            "ABC-202",
            startedAt.AddMinutes(2),
            new LiveSessionMetadata(
                "fake-thread-202",
                "turn-3",
                lastCodexEvent: "retry_scheduled",
                lastCodexTimestamp: startedAt.AddMinutes(2),
                lastCodexMessage: "Rate limit exceeded on the last attempt.",
                codexInputTokens: 144,
                codexOutputTokens: 36,
                codexTotalTokens: 180,
                estimatedInputTokens: 144,
                estimatedOutputTokens: 32,
                estimatedTotalTokens: 176,
                lastReportedInputTokens: 144,
                lastReportedOutputTokens: 36,
                lastReportedTotalTokens: 180,
                tokenComparisonStatus: SessionTokenComparisonStatus.Mismatch,
                tokenInputDelta: 0,
                tokenOutputDelta: 4,
                tokenTotalDelta: 4,
                lastEstimatedTokenAt: startedAt.AddMinutes(1),
                lastReportedTokenAt: startedAt.AddMinutes(2),
                turnCount: 3),
            attempt: 3,
            orchestratorSessionId: "orch-fake-202");
        store.RecordActivity("ABC-202", new SessionActivityEntry(SessionActivityKind.LifecycleMilestone, startedAt, "Session started", "Retry scenario seeded for dashboard validation."));
        store.RecordActivity("ABC-202", new SessionActivityEntry(SessionActivityKind.Warning, startedAt.AddMinutes(2), "Queued for retry", "Rate limit exceeded on the last attempt."));
        store.RecordSessionEnd("ABC-202", startedAt.AddMinutes(2), "Retrying", "Rate limit exceeded on the last attempt.");
    }

    private static void SeedBlockedSession(SessionActivityStore store)
    {
        var startedAt = new DateTimeOffset(2026, 4, 1, 8, 50, 0, TimeSpan.Zero);
        var blockedAt = startedAt.AddMinutes(2);
        store.RecordSessionStart("ABC-303", startedAt, "https://example.invalid/issues/ABC-303");
        store.RecordSessionMetadata(
            "ABC-303",
            blockedAt,
            new LiveSessionMetadata(
                "fake-thread-303",
                "turn-4",
                lastCodexEvent: "manual_decision_required",
                lastCodexTimestamp: blockedAt,
                lastCodexMessage: "Deployment target requires a manual approval step.",
                codexInputTokens: 410,
                codexOutputTokens: 152,
                codexTotalTokens: 562,
                estimatedInputTokens: 388,
                estimatedOutputTokens: 150,
                estimatedTotalTokens: 538,
                lastReportedInputTokens: 410,
                lastReportedOutputTokens: 152,
                lastReportedTotalTokens: 562,
                tokenComparisonStatus: SessionTokenComparisonStatus.Mismatch,
                tokenInputDelta: 22,
                tokenOutputDelta: 2,
                tokenTotalDelta: 24,
                lastEstimatedTokenAt: blockedAt.AddSeconds(-20),
                lastReportedTokenAt: blockedAt,
                turnCount: 4),
            attempt: 2,
            orchestratorSessionId: "orch-fake-303");
        store.RecordActivity("ABC-303", new SessionActivityEntry(SessionActivityKind.LifecycleMilestone, startedAt, "Session started", "Manual decision scenario seeded for follow-up validation."));
        store.RecordActivity("ABC-303", new SessionActivityEntry(SessionActivityKind.AttentionRequired, blockedAt, "Follow-up action created", "Review the staged deployment plan, then resolve the follow-up action to resume the session."));
        store.RecordSessionEnd("ABC-303", blockedAt, "Needs attention", "Deployment target requires a manual approval step.");
    }

    private static void SeedFailedDebugSession(SessionActivityStore store)
    {
        var startedAt = new DateTimeOffset(2026, 4, 1, 8, 55, 0, TimeSpan.Zero);
        var failedAt = startedAt.AddMinutes(3);
        var threadId = "fake-thread-404";
        var turnId = "fake-thread-404-turn-3";
        var largePrompt = BuildLargeUserPrompt();
        var deltaChunks = BuildAgentMessageDeltaChunks();
        var completedMessage = string.Concat(deltaChunks);

        store.RecordSessionStart("ABC-404", startedAt, "https://example.invalid/issues/ABC-404");
        store.RecordSessionMetadata(
            "ABC-404",
            failedAt,
            new LiveSessionMetadata(
                threadId,
                "turn-3",
                lastCodexEvent: "turn_failed",
                lastCodexTimestamp: failedAt,
                lastCodexMessage: "Prompt build failed after loading a large diagnostics payload.",
                codexInputTokens: 2384,
                codexOutputTokens: 912,
                codexTotalTokens: 3296,
                estimatedInputTokens: 2240,
                estimatedOutputTokens: 876,
                estimatedTotalTokens: 3116,
                lastReportedInputTokens: 2384,
                lastReportedOutputTokens: 912,
                lastReportedTotalTokens: 3296,
                tokenComparisonStatus: SessionTokenComparisonStatus.Mismatch,
                tokenInputDelta: 144,
                tokenOutputDelta: 36,
                tokenTotalDelta: 180,
                lastEstimatedTokenAt: startedAt.AddSeconds(30),
                lastReportedTokenAt: startedAt.AddSeconds(31),
                turnCount: 3),
            attempt: 1,
            orchestratorSessionId: "orch-fake-404");
        store.RecordActivity("ABC-404", new SessionActivityEntry(SessionActivityKind.LifecycleMilestone, startedAt, "Session started", "Codex-like large payload scenario seeded for debug mode validation."));
        store.RecordActivity("ABC-404", new SessionActivityEntry(SessionActivityKind.DebugMessage, startedAt.AddSeconds(2), "Sent initialize", """
                {
                  "id": 1,
                  "method": "initialize",
                  "params": {
                    "clientInfo": {
                      "name": "symphony",
                      "version": "1.0.0"
                    },
                    "capabilities": {}
                  }
                }
                """));
        store.RecordActivity("ABC-404", new SessionActivityEntry(SessionActivityKind.DebugMessage, startedAt.AddSeconds(3), "Received response 1", """
                {
                  "id": 1,
                  "result": {
                    "userAgent": "codex_vscode/0.94.0-alpha.7 (Mac OS 26.2.0; arm64) vscode/2.4.22 (codex_vscode; 0.1.0)"
                  }
                }
                """));
        store.RecordActivity("ABC-404", new SessionActivityEntry(SessionActivityKind.DebugMessage, startedAt.AddSeconds(4), "Sent initialized", """
                {
                  "method": "initialized",
                  "params": {}
                }
                """));
        store.RecordActivity("ABC-404", new SessionActivityEntry(SessionActivityKind.DebugMessage, startedAt.AddSeconds(5), "Sent thread/start", """
                {
                  "id": 2,
                  "method": "thread/start",
                  "params": {
                    "approvalPolicy": "never",
                    "cwd": "/workspace/fake-abc-404",
                    "sandbox": {
                      "type": "workspaceWrite",
                      "networkAccess": false
                    }
                  }
                }
                """));
        store.RecordActivity("ABC-404", new SessionActivityEntry(SessionActivityKind.DebugMessage, startedAt.AddSeconds(6), "Received response 2", SerializeDebugPayload(new { id = 2, result = new { thread = new { id = threadId } } })));
        store.RecordActivity("ABC-404", new SessionActivityEntry(SessionActivityKind.DebugMessage, startedAt.AddSeconds(8), "Sent turn/start", SerializeDebugPayload(new { id = 3, method = "turn/start", @params = new { threadId, cwd = "/workspace/fake-abc-404", title = "ABC-404: Investigate failing diagnostics import", approvalPolicy = "never", sandboxPolicy = new { type = "workspaceWrite", networkAccess = false }, input = new object[] { new { type = "text", text = largePrompt } } } })));
        store.RecordActivity("ABC-404", new SessionActivityEntry(SessionActivityKind.DebugMessage, startedAt.AddSeconds(9), "Received response 3", SerializeDebugPayload(new { id = 3, result = new { turn = new { id = turnId } } })));
        store.RecordActivity("ABC-404", new SessionActivityEntry(SessionActivityKind.DebugMessage, startedAt.AddSeconds(10), "Received thread/started", SerializeDebugPayload(new { method = "thread/started", @params = new { threadId } })));
        store.RecordActivity("ABC-404", new SessionActivityEntry(SessionActivityKind.DebugMessage, startedAt.AddSeconds(11), "Received turn/started", SerializeDebugPayload(new { method = "turn/started", @params = new { threadId, turnId } })));
        store.RecordActivity("ABC-404", new SessionActivityEntry(SessionActivityKind.DebugMessage, startedAt.AddSeconds(12), "Received item/started", SerializeDebugPayload(new { method = "item/started", @params = new { threadId, turnId, item = new { id = "item-user-message-1", type = "userMessage", role = "user", text = largePrompt } } })));

        for (var index = 0; index < deltaChunks.Length; index++)
        {
            store.RecordActivity(
                "ABC-404",
                new SessionActivityEntry(
                    SessionActivityKind.DebugMessage,
                    startedAt.AddSeconds(20 + index),
                    "Received item/agentMessage/delta",
                    SerializeDebugPayload(new { method = "item/agentMessage/delta", @params = new { threadId, turnId, itemId = "item-agent-message-1", deltaIndex = index, delta = deltaChunks[index] } })));
        }

        store.RecordActivity("ABC-404", new SessionActivityEntry(SessionActivityKind.DebugMessage, startedAt.AddSeconds(30), "Received item/completed", SerializeDebugPayload(new { method = "item/completed", @params = new { threadId, turnId, item = new { id = "item-agent-message-1", type = "agentMessage", role = "assistant", content = new object[] { new { type = "output_text", text = completedMessage } } } } })));
        store.RecordActivity("ABC-404", new SessionActivityEntry(SessionActivityKind.DebugMessage, startedAt.AddSeconds(31), "Received turn/completed", SerializeDebugPayload(new { method = "turn/completed", @params = new { threadId, turnId, usage = new { input_tokens = 2384, output_tokens = 912, total_tokens = 3296 }, message = "Diagnostics summary completed." } })));
        store.RecordActivity("ABC-404", new SessionActivityEntry(SessionActivityKind.Error, failedAt, "Prompt build failed", "Prompt build failed after loading a large diagnostics payload."));
        store.RecordSessionEnd("ABC-404", failedAt, "Failed", "Prompt build failed after loading a large diagnostics payload.");
    }

    private static void SeedSucceededSession(SessionActivityStore store)
    {
        var startedAt = new DateTimeOffset(2026, 4, 1, 8, 30, 0, TimeSpan.Zero);
        var endedAt = startedAt.AddMinutes(6);
        store.RecordSessionStart("ABC-505", startedAt, "https://example.invalid/issues/ABC-505");
        store.RecordSessionMetadata(
            "ABC-505",
            endedAt,
            new LiveSessionMetadata(
                "fake-thread-505",
                "turn-5",
                lastCodexEvent: "turn_completed",
                lastCodexTimestamp: endedAt,
                lastCodexMessage: "Published a clean success result.",
                codexInputTokens: 620,
                codexOutputTokens: 240,
                codexTotalTokens: 860,
                estimatedInputTokens: 620,
                estimatedOutputTokens: 240,
                estimatedTotalTokens: 860,
                lastReportedInputTokens: 620,
                lastReportedOutputTokens: 240,
                lastReportedTotalTokens: 860,
                tokenComparisonStatus: SessionTokenComparisonStatus.Match,
                tokenInputDelta: 0,
                tokenOutputDelta: 0,
                tokenTotalDelta: 0,
                lastEstimatedTokenAt: endedAt.AddSeconds(-10),
                lastReportedTokenAt: endedAt,
                turnCount: 5),
            attempt: 2,
            orchestratorSessionId: "orch-fake-505");
        store.RecordActivity("ABC-505", new SessionActivityEntry(SessionActivityKind.LifecycleMilestone, startedAt, "Session started", "Success scenario seeded for list/detail validation."));
        store.RecordActivity("ABC-505", new SessionActivityEntry(SessionActivityKind.Outcome, endedAt, "Run succeeded", "Published a clean success result."));
        store.RecordSessionEnd("ABC-505", endedAt, "Succeeded");
    }

    private static string BuildLargeUserPrompt()
    {
        return string.Join(
            "\n",
            [
                "Investigate the diagnostics import failure for ABC-404.",
                "Summarize the failing execution, the likely root cause, and the safest next steps.",
                "Use the following structure in the answer:",
                "1. Short diagnosis",
                "2. Evidence",
                "3. Affected files",
                "4. Suggested remediation",
                "",
                "Diagnostics bundle excerpts:",
                .. Enumerable.Range(1, 24).Select(index =>
                    $" - diagnostics/trace-{index:00}.json: stack frame {index:00}, import path /workspace/fake-abc-404/src/module-{index:00}.ts, note=payload chunk {index:000}")
            ]);
    }

    private static string[] BuildAgentMessageDeltaChunks()
    {
        return
        [
            "Diagnosis: the import pipeline is loading a diagnostics bundle whose JSON payload now exceeds the UI preview assumptions.\n\n",
            "Evidence:\n- initialize/thread/start/turn/start all succeeded\n- the large prompt and diagnostics bundle were accepted by the fake harness\n- repeated payload chunks show long nested traces and multi-file references\n\n",
            "Affected files:\n- src/module-07.ts\n- src/module-11.ts\n- src/module-18.ts\n- config/diagnostics-import.json\n\n",
            string.Join(
                "\n",
                Enumerable.Range(1, 36).Select(index =>
                    $"Trace sample {index:00}: module=module-{(index % 18) + 1:00}, stage=diagnostics-import, message=Captured long payload segment {index:000} with nested stack frames and config snapshots.")) + "\n\n",
            "Suggested remediation:\n1. preserve raw payload rendering behind debug mode only\n2. keep method filters available for item/agentMessage/delta\n3. add truncation only to summaries, never to the raw payload view\n4. validate the page with a transcript long enough to require scrolling in the details expander\n"
        ];
    }

    private static string SerializeDebugPayload(object payload)
    {
        return JsonSerializer.Serialize(
            payload,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });
    }
}
