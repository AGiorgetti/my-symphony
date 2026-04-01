using Microsoft.Extensions.Logging;
using Symphony.Abstractions.Orchestration;
using Symphony.Application.Orchestration;
using Symphony.Application.Runtime;

namespace Symphony.Host.Dashboard;

public sealed class FakeDashboardPageDataSource
{
    private readonly object _sync = new();
    private readonly SessionActivityStore _sessionActivityStore;
    private FakeDashboardFixtureState _state;

    public FakeDashboardPageDataSource(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _sessionActivityStore = new SessionActivityStore(loggerFactory.CreateLogger<SessionActivityStore>());
        _state = CreateFixtureState(_sessionActivityStore);
    }

    public Task<DashboardSnapshot> GetDashboardSnapshotAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(_state.DashboardSnapshot);
        }
    }

    public IReadOnlyList<SessionRecord> GetAllSessions()
    {
        lock (_sync)
        {
            return _sessionActivityStore.GetAllSessions();
        }
    }

    public SessionRecord? GetSession(string issueIdentifier)
    {
        lock (_sync)
        {
            return _sessionActivityStore.GetSession(issueIdentifier);
        }
    }

    public IReadOnlyList<SessionActivityEntry> GetActivities(string issueIdentifier)
    {
        lock (_sync)
        {
            return _sessionActivityStore.GetActivities(issueIdentifier);
        }
    }

    public Task<OrchestratorIssueSnapshot?> GetIssueSnapshotAsync(
        string issueIdentifier,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(
                _state.IssueSnapshots.TryGetValue(issueIdentifier, out var snapshot)
                    ? snapshot
                    : null);
        }
    }

    public Task<FollowUpActionResolutionResult> ResolveFollowUpActionAsync(
        FollowUpActionResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_state.IssueSnapshots.TryGetValue(request.IssueIdentifier, out var issueSnapshot))
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
                FollowUpActions = [resolvedAction]
            };

            _state = _state with
            {
                DashboardSnapshot = _state.DashboardSnapshot with
                {
                    GeneratedAt = resolvedAt,
                    RunningCount = 2,
                    BlockedCount = 0,
                    ActiveSessions =
                    [
                        _state.DashboardSnapshot.ActiveSessions[0],
                        new DashboardActiveSessionSnapshot(
                            issueSnapshot.IssueIdentifier,
                            "StreamingTurn",
                            resumedRunningSnapshot.SessionId,
                            resumedRunningSnapshot.TurnCount,
                            resumedRunningSnapshot.LastEvent,
                            resumedRunningSnapshot.LastMessage,
                            resumedRunningSnapshot.StartedAt,
                            resumedRunningSnapshot.LastEventAt,
                            resumedRunningSnapshot.TotalTokens)
                    ],
                    BlockedSessions = [],
                    FollowUpActions =
                    [
                        resolvedAction,
                        .. (_state.DashboardSnapshot.FollowUpActions ?? Array.Empty<FollowUpActionSnapshot>())
                            .Where(action => !string.Equals(action.FollowUpActionId, resolvedAction.FollowUpActionId, StringComparison.OrdinalIgnoreCase))
                    ],
                    RecentAttempts =
                    [
                        new DashboardRecentAttemptSnapshot(
                            issueSnapshot.IssueIdentifier,
                            resumedIssue.CurrentRetryAttempt,
                            "Resumed",
                            resolvedAt,
                            18d,
                            null,
                            resumedRunningSnapshot.SessionId,
                            resumedIssue.OrchestratorSessionId),
                        .. _state.DashboardSnapshot.RecentAttempts
                    ]
                },
                IssueSnapshots = new Dictionary<string, OrchestratorIssueSnapshot>(_state.IssueSnapshots, StringComparer.OrdinalIgnoreCase)
                {
                    [issueSnapshot.IssueIdentifier] = resumedIssue
                }
            };

            _sessionActivityStore.RecordSessionStart(
                issueSnapshot.IssueIdentifier,
                resumedRunningSnapshot.StartedAt,
                $"https://example.invalid/issues/{issueSnapshot.IssueIdentifier}");
            _sessionActivityStore.RecordActivity(
                issueSnapshot.IssueIdentifier,
                new SessionActivityEntry(
                    SessionActivityKind.LifecycleMilestone,
                    resolvedAt,
                    "Follow-up action resolved",
                    $"Operator selected '{selectedOptionId}' in fake mode."));
            _sessionActivityStore.RecordActivity(
                issueSnapshot.IssueIdentifier,
                new SessionActivityEntry(
                    SessionActivityKind.AgentMessage,
                    resolvedAt.AddSeconds(5),
                    "Session resumed",
                    "Fake mode resumed the blocked session for UI validation."));

            return Task.FromResult(
                new FollowUpActionResolutionResult(
                    FollowUpActionResolutionStatus.Resolved,
                    Requeued: true,
                    Action: resolvedAction,
                    Message: "Fake mode resumed the blocked session."));
        }
    }

    private static FakeDashboardFixtureState CreateFixtureState(SessionActivityStore sessionActivityStore)
    {
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

        return new FakeDashboardFixtureState(dashboardSnapshot, issueSnapshots);
    }

    private static void SeedActiveSession(SessionActivityStore store)
    {
        var startedAt = new DateTimeOffset(2026, 4, 1, 8, 38, 0, TimeSpan.Zero);
        store.RecordSessionStart("ABC-101", startedAt, "https://example.invalid/issues/ABC-101");
        store.RecordActivity("ABC-101", new SessionActivityEntry(SessionActivityKind.LifecycleMilestone, startedAt, "Session started", "Fake mode bootstrapped an active dashboard scenario."));
        store.RecordActivity("ABC-101", new SessionActivityEntry(SessionActivityKind.ProgressUpdate, startedAt.AddMinutes(8), "Workspace hydrated", "Loaded design notes and previous attempts."));
        store.RecordActivity("ABC-101", new SessionActivityEntry(SessionActivityKind.Warning, startedAt.AddMinutes(14), "Minor warning", "A non-blocking lint warning is waiting for cleanup."));
        store.RecordActivity("ABC-101", new SessionActivityEntry(SessionActivityKind.AgentMessage, startedAt.AddMinutes(21), "turn_completed", "Refined the dashboard filters and queued one follow-up."));
    }

    private static void SeedRetryingSession(SessionActivityStore store)
    {
        var startedAt = new DateTimeOffset(2026, 4, 1, 8, 49, 0, TimeSpan.Zero);
        store.RecordSessionStart("ABC-202", startedAt, "https://example.invalid/issues/ABC-202");
        store.RecordActivity("ABC-202", new SessionActivityEntry(SessionActivityKind.LifecycleMilestone, startedAt, "Session started", "Retry scenario seeded for dashboard validation."));
        store.RecordActivity("ABC-202", new SessionActivityEntry(SessionActivityKind.Warning, startedAt.AddMinutes(2), "Queued for retry", "Rate limit exceeded on the last attempt."));
        store.RecordSessionEnd("ABC-202", startedAt.AddMinutes(2), "Retrying", "Rate limit exceeded on the last attempt.");
    }

    private static void SeedBlockedSession(SessionActivityStore store)
    {
        var startedAt = new DateTimeOffset(2026, 4, 1, 8, 50, 0, TimeSpan.Zero);
        var blockedAt = startedAt.AddMinutes(2);
        store.RecordSessionStart("ABC-303", startedAt, "https://example.invalid/issues/ABC-303");
        store.RecordActivity("ABC-303", new SessionActivityEntry(SessionActivityKind.LifecycleMilestone, startedAt, "Session started", "Manual decision scenario seeded for follow-up validation."));
        store.RecordActivity("ABC-303", new SessionActivityEntry(SessionActivityKind.AttentionRequired, blockedAt, "Follow-up action created", "Review the staged deployment plan, then resolve the follow-up action to resume the session."));
        store.RecordSessionEnd("ABC-303", blockedAt, "Needs attention", "Deployment target requires a manual approval step.");
    }

    private static void SeedFailedDebugSession(SessionActivityStore store)
    {
        var startedAt = new DateTimeOffset(2026, 4, 1, 8, 55, 0, TimeSpan.Zero);
        var failedAt = startedAt.AddMinutes(3);
        store.RecordSessionStart("ABC-404", startedAt, "https://example.invalid/issues/ABC-404");
        store.RecordActivity("ABC-404", new SessionActivityEntry(SessionActivityKind.LifecycleMilestone, startedAt, "Session started", "Large payload scenario seeded for debug mode validation."));
        store.RecordActivity(
            "ABC-404",
            new SessionActivityEntry(
                SessionActivityKind.DebugMessage,
                startedAt.AddMinutes(1),
                "Sent turn/start",
                """
                {
                  "id": 18,
                  "method": "turn/start",
                  "params": {
                    "threadId": "fake-thread-404-turn-3",
                    "input": [
                      {
                        "type": "text",
                        "text": "Summarize the diagnostics bundle and explain the failure."
                      }
                    ]
                  }
                }
                """));
        store.RecordActivity(
            "ABC-404",
            new SessionActivityEntry(
                SessionActivityKind.DebugMessage,
                startedAt.AddMinutes(1).AddSeconds(1),
                "Received turn/completed",
                """
                {
                  "method": "turn/completed",
                  "params": {
                    "message": "loaded",
                    "usage": {
                      "input_tokens": 280,
                      "output_tokens": 84,
                      "total_tokens": 364
                    }
                  }
                }
                """));
        store.RecordActivity(
            "ABC-404",
            new SessionActivityEntry(
                SessionActivityKind.DebugMessage,
                startedAt.AddMinutes(1).AddSeconds(2),
                "Received item/agentMessage/delta",
                """
                {
                  "method": "item/agentMessage/delta",
                  "params": {
                    "delta": "Diagnostics excerpt: stack trace line 1, stack trace line 2, configuration snapshot line 3..."
                  }
                }
                """));
        store.RecordActivity("ABC-404", new SessionActivityEntry(SessionActivityKind.Error, failedAt, "Prompt build failed", "Prompt build failed after loading a large diagnostics payload."));
        store.RecordSessionEnd("ABC-404", failedAt, "Failed", "Prompt build failed after loading a large diagnostics payload.");
    }

    private static void SeedSucceededSession(SessionActivityStore store)
    {
        var startedAt = new DateTimeOffset(2026, 4, 1, 8, 30, 0, TimeSpan.Zero);
        var endedAt = startedAt.AddMinutes(6);
        store.RecordSessionStart("ABC-505", startedAt, "https://example.invalid/issues/ABC-505");
        store.RecordActivity("ABC-505", new SessionActivityEntry(SessionActivityKind.LifecycleMilestone, startedAt, "Session started", "Success scenario seeded for list/detail validation."));
        store.RecordActivity("ABC-505", new SessionActivityEntry(SessionActivityKind.Outcome, endedAt, "Run succeeded", "Published a clean success result."));
        store.RecordSessionEnd("ABC-505", endedAt, "Succeeded");
    }

    private sealed record FakeDashboardFixtureState(
        DashboardSnapshot DashboardSnapshot,
        IReadOnlyDictionary<string, OrchestratorIssueSnapshot> IssueSnapshots);
}
