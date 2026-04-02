using System.Text.Json;
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
        var threadId = "fake-thread-404";
        var turnId = "fake-thread-404-turn-3";
        var largePrompt = BuildLargeUserPrompt();
        var deltaChunks = BuildAgentMessageDeltaChunks();
        var completedMessage = string.Concat(deltaChunks);

        store.RecordSessionStart("ABC-404", startedAt, "https://example.invalid/issues/ABC-404");
        store.RecordActivity("ABC-404", new SessionActivityEntry(SessionActivityKind.LifecycleMilestone, startedAt, "Session started", "Codex-like large payload scenario seeded for debug mode validation."));
        store.RecordActivity(
            "ABC-404",
            new SessionActivityEntry(
                SessionActivityKind.DebugMessage,
                startedAt.AddSeconds(2),
                "Sent initialize",
                """
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
        store.RecordActivity(
            "ABC-404",
            new SessionActivityEntry(
                SessionActivityKind.DebugMessage,
                startedAt.AddSeconds(3),
                "Received response 1",
                """
                {
                  "id": 1,
                  "result": {
                    "userAgent": "codex_vscode/0.94.0-alpha.7 (Mac OS 26.2.0; arm64) vscode/2.4.22 (codex_vscode; 0.1.0)"
                  }
                }
                """));
        store.RecordActivity(
            "ABC-404",
            new SessionActivityEntry(
                SessionActivityKind.DebugMessage,
                startedAt.AddSeconds(4),
                "Sent initialized",
                """
                {
                  "method": "initialized",
                  "params": {}
                }
                """));
        store.RecordActivity(
            "ABC-404",
            new SessionActivityEntry(
                SessionActivityKind.DebugMessage,
                startedAt.AddSeconds(5),
                "Sent thread/start",
                """
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
        store.RecordActivity(
            "ABC-404",
            new SessionActivityEntry(
                SessionActivityKind.DebugMessage,
                startedAt.AddSeconds(6),
                "Received response 2",
                SerializeDebugPayload(
                    new
                    {
                        id = 2,
                        result = new
                        {
                            thread = new
                            {
                                id = threadId
                            }
                        }
                    })));
        store.RecordActivity(
            "ABC-404",
            new SessionActivityEntry(
                SessionActivityKind.DebugMessage,
                startedAt.AddSeconds(8),
                "Sent turn/start",
                SerializeDebugPayload(
                    new
                    {
                        id = 3,
                        method = "turn/start",
                        @params = new
                        {
                            threadId,
                            cwd = "/workspace/fake-abc-404",
                            title = "ABC-404: Investigate failing diagnostics import",
                            approvalPolicy = "never",
                            sandboxPolicy = new
                            {
                                type = "workspaceWrite",
                                networkAccess = false
                            },
                            input = new object[]
                            {
                                new
                                {
                                    type = "text",
                                    text = largePrompt
                                }
                            }
                        }
                    })));
        store.RecordActivity(
            "ABC-404",
            new SessionActivityEntry(
                SessionActivityKind.DebugMessage,
                startedAt.AddSeconds(9),
                "Received response 3",
                SerializeDebugPayload(
                    new
                    {
                        id = 3,
                        result = new
                        {
                            turn = new
                            {
                                id = turnId
                            }
                        }
                    })));
        store.RecordActivity(
            "ABC-404",
            new SessionActivityEntry(
                SessionActivityKind.DebugMessage,
                startedAt.AddSeconds(10),
                "Received thread/started",
                SerializeDebugPayload(
                    new
                    {
                        method = "thread/started",
                        @params = new
                        {
                            threadId
                        }
                    })));
        store.RecordActivity(
            "ABC-404",
            new SessionActivityEntry(
                SessionActivityKind.DebugMessage,
                startedAt.AddSeconds(11),
                "Received turn/started",
                SerializeDebugPayload(
                    new
                    {
                        method = "turn/started",
                        @params = new
                        {
                            threadId,
                            turnId
                        }
                    })));
        store.RecordActivity(
            "ABC-404",
            new SessionActivityEntry(
                SessionActivityKind.DebugMessage,
                startedAt.AddSeconds(12),
                "Received item/started",
                SerializeDebugPayload(
                    new
                    {
                        method = "item/started",
                        @params = new
                        {
                            threadId,
                            turnId,
                            item = new
                            {
                                id = "item-user-message-1",
                                type = "userMessage",
                                role = "user",
                                text = largePrompt
                            }
                        }
                    })));
        for (var index = 0; index < deltaChunks.Length; index++)
        {
            store.RecordActivity(
                "ABC-404",
                new SessionActivityEntry(
                    SessionActivityKind.DebugMessage,
                    startedAt.AddSeconds(20 + index),
                    "Received item/agentMessage/delta",
                    SerializeDebugPayload(
                        new
                        {
                            method = "item/agentMessage/delta",
                            @params = new
                            {
                                threadId,
                                turnId,
                                itemId = "item-agent-message-1",
                                deltaIndex = index,
                                delta = deltaChunks[index]
                            }
                        })));
        }
        store.RecordActivity(
            "ABC-404",
            new SessionActivityEntry(
                SessionActivityKind.DebugMessage,
                startedAt.AddSeconds(30),
                "Received item/completed",
                SerializeDebugPayload(
                    new
                    {
                        method = "item/completed",
                        @params = new
                        {
                            threadId,
                            turnId,
                            item = new
                            {
                                id = "item-agent-message-1",
                                type = "agentMessage",
                                role = "assistant",
                                content = new object[]
                                {
                                    new
                                    {
                                        type = "output_text",
                                        text = completedMessage
                                    }
                                }
                            }
                        }
                    })));
        store.RecordActivity(
            "ABC-404",
            new SessionActivityEntry(
                SessionActivityKind.DebugMessage,
                startedAt.AddSeconds(31),
                "Received turn/completed",
                SerializeDebugPayload(
                    new
                    {
                        method = "turn/completed",
                        @params = new
                        {
                            threadId,
                            turnId,
                            usage = new
                            {
                                input_tokens = 2384,
                                output_tokens = 912,
                                total_tokens = 3296
                            },
                            message = "Diagnostics summary completed."
                        }
                    })));
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
