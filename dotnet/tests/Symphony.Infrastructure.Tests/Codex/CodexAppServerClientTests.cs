using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Symphony.Abstractions.Orchestration;
using Symphony.Application.Configuration;
using Symphony.Application.Orchestration;
using Symphony.Domain.Issues;
using Symphony.Domain.Runs;
using Symphony.Domain.Sessions;
using Symphony.Infrastructure.Codex;

namespace Symphony.Infrastructure.Tests.Codex;

public sealed class CodexAppServerClientTests
{
    [Fact]
    public async Task RunAsync_sends_handshake_updates_session_and_handles_approval_and_tool_calls()
    {
        var transcriptSink = new RecordingTranscriptSink();
        var sessionFactory = new TestCodexProcessSessionFactory(
            async (line, session) =>
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var method = root.TryGetProperty("method", out var methodElement)
                    ? methodElement.GetString()
                    : null;

                if (method == "initialize")
                {
                    session.EnqueueStdout(new { id = 1, result = new { } });
                    return;
                }

                if (method == "thread/start")
                {
                    session.EnqueueStdout(new { id = 2, result = new { thread = new { id = "thread-123" } } });
                    return;
                }

                if (method == "turn/start")
                {
                    session.EnqueueStdout(new { id = 3, result = new { turn = new { id = "turn-456" } } });
                    session.EnqueueStdout(new { id = "approval-1", method = "approval/request", @params = new { reason = "shell" } });
                    session.EnqueueStdout(new { id = "tool-1", method = "item/tool/call", @params = new { name = "unsupported" } });
                    session.EnqueueStdout(new
                    {
                        method = "turn/completed",
                        @params = new
                        {
                            message = "done",
                            usage = new
                            {
                                input_tokens = 12,
                                output_tokens = 5,
                                total_tokens = 17
                            }
                        }
                    });
                }

                await Task.CompletedTask;
            });
        var client = CreateClient(sessionFactory, transcriptSink);
        using var testContext = CreateContext();

        await client.RunAsync(testContext.Context, Path.GetTempPath(), "Prompt body", CreateCodexOptions());

        var startRequest = Assert.Single(sessionFactory.Requests);
        Assert.Equal("codex app-server", startRequest.Command);
        Assert.Equal(Path.GetTempPath(), startRequest.WorkingDirectory);
        Assert.Equal(
            [RunAttemptStatus.InitializingSession, RunAttemptStatus.StreamingTurn],
            testContext.Logger.Entries
                .Where(entry => entry.Message.Contains("session_status completed", StringComparison.Ordinal))
                .Select(entry => Assert.IsType<RunAttemptStatus>(entry.State["status"]))
                .ToArray());

        var latestSession = Assert.IsType<LiveSessionMetadata>(testContext.Context.Session);
        Assert.Equal("thread-123-turn-456", latestSession.SessionId);
        Assert.Equal("turn_completed", latestSession.LastCodexEvent);
        Assert.Equal("done", latestSession.LastCodexMessage);
        Assert.Equal(12, latestSession.CodexInputTokens);
        Assert.Equal(5, latestSession.CodexOutputTokens);
        Assert.Equal(17, latestSession.CodexTotalTokens);

        Assert.NotNull(sessionFactory.Session);
        var turnStartLine = sessionFactory.Session!.SentLines.Single(
            line => line.Contains("\"method\":\"turn/start\"", StringComparison.Ordinal));
        using var turnStartDocument = JsonDocument.Parse(turnStartLine);
        var sandboxPolicy = turnStartDocument.RootElement.GetProperty("params").GetProperty("sandboxPolicy");
        Assert.Equal("workspaceWrite", sandboxPolicy.GetProperty("type").GetString());
        Assert.True(sandboxPolicy.GetProperty("networkAccess").GetBoolean());

        Assert.Contains(
            sessionFactory.Session.SentLines,
            line => line.Contains("\"approved\":true", StringComparison.Ordinal));
        Assert.Contains(
            sessionFactory.Session.SentLines,
            line => line.Contains("\"error\":\"unsupported_tool_call\"", StringComparison.Ordinal));
        Assert.True(sessionFactory.Session.WasKilled);
        Assert.Contains(
            transcriptSink.Outbound,
            entry => entry.Title == "Sent initialize" && entry.Payload.Contains("\"method\":\"initialize\"", StringComparison.Ordinal));
        Assert.Contains(
            transcriptSink.Outbound,
            entry => entry.Title == "Sent turn/start" && entry.Payload.Contains("Prompt body", StringComparison.Ordinal));
        Assert.Contains(
            transcriptSink.Outbound,
            entry => entry.Title == "Sent response approval-1" && entry.Payload.Contains("\"approved\":true", StringComparison.Ordinal));
        Assert.Contains(
            transcriptSink.Inbound,
            entry => entry.Title == "Received response 1" && entry.Payload.Contains("\"id\":1", StringComparison.Ordinal));
        Assert.Contains(
            transcriptSink.Inbound,
            entry => entry.Title == "Received approval/request");
        Assert.Contains(
            transcriptSink.Inbound,
            entry => entry.Title == "Received turn/completed" && entry.Payload.Contains("\"message\":\"done\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_blocks_when_codex_requests_user_input()
    {
        var transcriptSink = new RecordingTranscriptSink();
        var sessionFactory = new TestCodexProcessSessionFactory(
            async (line, session) =>
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var method = root.TryGetProperty("method", out var methodElement)
                    ? methodElement.GetString()
                    : null;

                if (method == "initialize")
                {
                    session.EnqueueStdout(new { id = 1, result = new { } });
                    return;
                }

                if (method == "thread/start")
                {
                    session.EnqueueStdout(new { id = 2, result = new { thread = new { id = "thread-123" } } });
                    return;
                }

                if (method == "turn/start")
                {
                    session.EnqueueStdout(new { id = 3, result = new { turn = new { id = "turn-456" } } });
                    session.EnqueueStdout(new { id = "input-1", method = "item/tool/requestUserInput", @params = new { prompt = "Need help" } });
                }

                await Task.CompletedTask;
            });
        var client = CreateClient(sessionFactory, transcriptSink);
        using var testContext = CreateContext();

        var exception = await Assert.ThrowsAsync<IssueBlockingException>(
            () => client.RunAsync(testContext.Context, Path.GetTempPath(), "Prompt body", CreateCodexOptions()));

        Assert.Equal(BlockingReasonCode.InputRequired, exception.ReasonCode);
        Assert.Equal("Provide the required input or choose an option, then resolve the follow-up action to resume the run.", exception.RequiredUserAction);
        Assert.NotNull(sessionFactory.Session);
        Assert.True(sessionFactory.Session!.WasKilled);
    }

    [Fact]
    public async Task RunAsync_auto_approves_mcp_tool_request_user_input_when_approval_policy_is_never()
    {
        var transcriptSink = new RecordingTranscriptSink();
        var sessionFactory = new TestCodexProcessSessionFactory(
            async (line, session) =>
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var method = root.TryGetProperty("method", out var methodElement)
                    ? methodElement.GetString()
                    : null;

                if (method == "initialize")
                {
                    session.EnqueueStdout(new { id = 1, result = new { } });
                    return;
                }

                if (method == "thread/start")
                {
                    session.EnqueueStdout(new { id = 2, result = new { thread = new { id = "thread-123" } } });
                    return;
                }

                if (method == "turn/start")
                {
                    session.EnqueueStdout(new { id = 3, result = new { turn = new { id = "turn-456" } } });
                    session.EnqueueStdout(new
                    {
                        id = 0,
                        method = "item/tool/requestUserInput",
                        @params = new
                        {
                            questions = new object[]
                            {
                                new
                                {
                                    id = "mcp_tool_call_approval_call-717",
                                    question = "Allow GitHub to add a comment to a pull request?",
                                    options = new object[]
                                    {
                                        new { label = "Allow" },
                                        new { label = "Allow for this session" },
                                        new { label = "Cancel" }
                                    }
                                }
                            }
                        }
                    });
                    return;
                }

                if (root.TryGetProperty("id", out var idElement)
                    && idElement.ValueKind == JsonValueKind.Number
                    && idElement.GetInt32() == 0)
                {
                    session.EnqueueStdout(new { method = "turn/completed", @params = new { message = "done" } });
                }

                await Task.CompletedTask;
            });
        var client = CreateClient(sessionFactory, transcriptSink);
        using var testContext = CreateContext();

        await client.RunAsync(testContext.Context, Path.GetTempPath(), "Prompt body", CreateCodexOptions());

        Assert.NotNull(sessionFactory.Session);
        var autoApprovalLine = sessionFactory.Session!.SentLines.Single(
            line => line.Contains("\"id\":0", StringComparison.Ordinal));
        using var approvalDocument = JsonDocument.Parse(autoApprovalLine);
        var answers = approvalDocument.RootElement
            .GetProperty("result")
            .GetProperty("answers")
            .GetProperty("mcp_tool_call_approval_call-717")
            .GetProperty("answers");

        Assert.Equal("Allow for this session", answers[0].GetString());
        Assert.Contains(
            transcriptSink.Outbound,
            entry => entry.Title == "Sent response 0"
                && entry.Payload.Contains("Allow for this session", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_auto_approves_mcp_tool_elicitation_request_when_approval_policy_is_never()
    {
        var transcriptSink = new RecordingTranscriptSink();
        var sessionFactory = new TestCodexProcessSessionFactory(
            async (line, session) =>
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var method = root.TryGetProperty("method", out var methodElement)
                    ? methodElement.GetString()
                    : null;

                if (method == "initialize")
                {
                    session.EnqueueStdout(new { id = 1, result = new { } });
                    return;
                }

                if (method == "thread/start")
                {
                    session.EnqueueStdout(new { id = 2, result = new { thread = new { id = "thread-123" } } });
                    return;
                }

                if (method == "turn/start")
                {
                    session.EnqueueStdout(new { id = 3, result = new { turn = new { id = "turn-456" } } });
                    session.EnqueueStdout(new
                    {
                        id = 0,
                        method = "mcpServer/elicitation/request",
                        @params = new
                        {
                            threadId = "019d2eb6-640f-7780-834c-d6ec7e4ffb9c",
                            turnId = "019d2eb6-643b-77c0-b5a8-4278f8cb8383",
                            serverName = "codex_apps",
                            mode = "form",
                            _meta = new
                            {
                                codex_approval_kind = "mcp_tool_call",
                                tool_title = "update_issue_comment"
                            },
                            message = "Allow GitHub to update an issue comment?",
                            requestedSchema = new
                            {
                                type = "object",
                                properties = new { }
                            }
                        }
                    });
                    return;
                }

                if (root.TryGetProperty("id", out var idElement)
                    && idElement.ValueKind == JsonValueKind.Number
                    && idElement.GetInt32() == 0)
                {
                    session.EnqueueStdout(new { method = "turn/completed", @params = new { message = "done" } });
                }

                await Task.CompletedTask;
            });
        var client = CreateClient(sessionFactory, transcriptSink);
        using var testContext = CreateContext();

        await client.RunAsync(testContext.Context, Path.GetTempPath(), "Prompt body", CreateCodexOptions());

        Assert.NotNull(sessionFactory.Session);
        var autoApprovalLine = sessionFactory.Session!.SentLines.Single(
            line => line.Contains("\"id\":0", StringComparison.Ordinal));
        using var approvalDocument = JsonDocument.Parse(autoApprovalLine);
        var result = approvalDocument.RootElement.GetProperty("result");

        Assert.Equal("accept", result.GetProperty("action").GetString());
        Assert.Equal(JsonValueKind.Object, result.GetProperty("content").ValueKind);
        Assert.Contains(
            transcriptSink.Outbound,
            entry => entry.Title == "Sent response 0"
                && entry.Payload.Contains("\"action\":\"accept\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_blocks_when_non_approvable_mcp_elicitation_requests_user_input()
    {
        var transcriptSink = new RecordingTranscriptSink();
        var sessionFactory = new TestCodexProcessSessionFactory(
            async (line, session) =>
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var method = root.TryGetProperty("method", out var methodElement)
                    ? methodElement.GetString()
                    : null;

                if (method == "initialize")
                {
                    session.EnqueueStdout(new { id = 1, result = new { } });
                    return;
                }

                if (method == "thread/start")
                {
                    session.EnqueueStdout(new { id = 2, result = new { thread = new { id = "thread-123" } } });
                    return;
                }

                if (method == "turn/start")
                {
                    session.EnqueueStdout(new { id = 3, result = new { turn = new { id = "turn-456" } } });
                    session.EnqueueStdout(new
                    {
                        id = 0,
                        method = "mcpServer/elicitation/request",
                        @params = new
                        {
                            mode = "form",
                            message = "Need a human choice",
                            requestedSchema = new
                            {
                                type = "object",
                                properties = new
                                {
                                    choice = new
                                    {
                                        type = "string"
                                    }
                                }
                            }
                        }
                    });
                }

                await Task.CompletedTask;
            });
        var client = CreateClient(sessionFactory, transcriptSink);
        using var testContext = CreateContext();

        var exception = await Assert.ThrowsAsync<IssueBlockingException>(
            () => client.RunAsync(testContext.Context, Path.GetTempPath(), "Prompt body", CreateCodexOptions()));

        Assert.Equal(BlockingReasonCode.ManualDecisionRequired, exception.ReasonCode);
        Assert.Equal("Need a human choice", exception.Message);
        Assert.Equal("Review the requested manual decision, then resolve the follow-up action to resume the run.", exception.RequiredUserAction);
        Assert.NotNull(sessionFactory.Session);
        Assert.True(sessionFactory.Session!.WasKilled);
    }

    [Fact]
    public async Task RunAsync_reuses_same_thread_for_continuation_turns_and_updates_turn_count()
    {
        var transcriptSink = new RecordingTranscriptSink();
        var turnCounter = 0;
        var sessionFactory = new TestCodexProcessSessionFactory(
            async (line, session) =>
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var method = root.TryGetProperty("method", out var methodElement)
                    ? methodElement.GetString()
                    : null;

                if (method == "initialize")
                {
                    session.EnqueueStdout(new { id = 1, result = new { } });
                    return;
                }

                if (method == "thread/start")
                {
                    session.EnqueueStdout(new { id = 2, result = new { thread = new { id = "thread-123" } } });
                    return;
                }

                if (method == "turn/start")
                {
                    turnCounter++;
                    session.EnqueueStdout(new { id = turnCounter + 2, result = new { turn = new { id = $"turn-{turnCounter}" } } });
                    session.EnqueueStdout(new { method = "turn/completed", @params = new { message = $"done-{turnCounter}" } });
                }

                await Task.CompletedTask;
            });
        var client = CreateClient(sessionFactory, transcriptSink);
        using var testContext = CreateContext();

        await client.RunAsync(
            testContext.Context,
            Path.GetTempPath(),
            "Prompt body",
            CreateCodexOptions(),
            (completedTurnCount, cancellationToken) => Task.FromResult<string?>(
                completedTurnCount == 1 ? "Continuation prompt" : null));

        Assert.NotNull(sessionFactory.Session);
        var turnStartLines = sessionFactory.Session!.SentLines
            .Where(line => line.Contains("\"method\":\"turn/start\"", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, turnStartLines.Length);

        using var firstTurnDocument = JsonDocument.Parse(turnStartLines[0]);
        using var secondTurnDocument = JsonDocument.Parse(turnStartLines[1]);
        var firstTurnRoot = firstTurnDocument.RootElement;
        var secondTurnRoot = secondTurnDocument.RootElement;
        var firstTurnParams = firstTurnRoot.GetProperty("params");
        var secondTurnParams = secondTurnRoot.GetProperty("params");

        Assert.Equal(3, firstTurnRoot.GetProperty("id").GetInt32());
        Assert.Equal(4, secondTurnRoot.GetProperty("id").GetInt32());
        Assert.Equal("thread-123", firstTurnParams.GetProperty("threadId").GetString());
        Assert.Equal("thread-123", secondTurnParams.GetProperty("threadId").GetString());
        Assert.Equal("Prompt body", firstTurnParams.GetProperty("input")[0].GetProperty("text").GetString());
        Assert.Equal("Continuation prompt", secondTurnParams.GetProperty("input")[0].GetProperty("text").GetString());

        var latestSession = Assert.IsType<LiveSessionMetadata>(testContext.Context.Session);
        Assert.Equal("thread-123-turn-2", latestSession.SessionId);
        Assert.Equal(2, latestSession.TurnCount);
        Assert.Equal("turn_completed", latestSession.LastCodexEvent);
        Assert.Equal("done-2", latestSession.LastCodexMessage);
    }

    [Fact]
    public async Task RunAsync_includes_stderr_when_process_exits_during_startup_handshake()
    {
        var transcriptSink = new RecordingTranscriptSink();
        var sessionFactory = new TestCodexProcessSessionFactory(
            async (line, session) =>
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var method = root.TryGetProperty("method", out var methodElement)
                    ? methodElement.GetString()
                    : null;

                if (method == "initialize")
                {
                    session.EnqueueStderr("codex: command not found");
                    session.Exit(127);
                }

                await Task.CompletedTask;
            });
        var client = CreateClient(sessionFactory, transcriptSink);
        using var testContext = CreateContext();

        var exception = await Assert.ThrowsAsync<CodexAgentException>(
            () => client.RunAsync(testContext.Context, Path.GetTempPath(), "Prompt body", CreateCodexOptions()));

        Assert.Equal("port_exit", exception.Code);
        Assert.Contains("startup handshake", exception.Message, StringComparison.Ordinal);
        Assert.Contains("exit_code=127", exception.Message, StringComparison.Ordinal);
        if (exception.Message.Contains("stderr=", StringComparison.Ordinal))
        {
            Assert.Contains("command not found", exception.Message, StringComparison.Ordinal);
        }
        Assert.Contains(
            transcriptSink.Diagnostics,
            entry => entry.Title == "Received stderr" && entry.Payload.Contains("command not found", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_skips_agent_message_delta_transcript_entries_when_delta_tracking_is_disabled()
    {
        var transcriptSink = new RecordingTranscriptSink(trackAgentMessageDeltas: false);
        var sessionFactory = new TestCodexProcessSessionFactory(
            async (line, session) =>
            {
                using var document = JsonDocument.Parse(line);
                var method = document.RootElement.TryGetProperty("method", out var methodElement)
                    ? methodElement.GetString()
                    : null;

                if (method == "initialize")
                {
                    session.EnqueueStdout(new { id = 1, result = new { } });
                    return;
                }

                if (method == "thread/start")
                {
                    session.EnqueueStdout(new { id = 2, result = new { thread = new { id = "thread-123" } } });
                    return;
                }

                if (method == "turn/start")
                {
                    session.EnqueueStdout(new { id = 3, result = new { turn = new { id = "turn-456" } } });
                    session.EnqueueStdout(new { method = "item/agentMessage/delta", @params = new { delta = "partial" } });
                    session.EnqueueStdout(new { method = "turn/completed", @params = new { message = "done" } });
                }

                await Task.CompletedTask;
            });
        var client = CreateClient(sessionFactory, transcriptSink);
        using var testContext = CreateContext();

        await client.RunAsync(testContext.Context, Path.GetTempPath(), "Prompt body", CreateCodexOptions());

        Assert.DoesNotContain(
            transcriptSink.Inbound,
            entry => entry.Title == "Received item/agentMessage/delta");
        Assert.Contains(
            transcriptSink.Inbound,
            entry => entry.Title == "Received turn/completed");
    }

    [Fact]
    public async Task RunAsync_records_estimated_and_reported_token_usage_in_session_metadata()
    {
        var transcriptSink = new RecordingTranscriptSink();
        var sessionFactory = new TestCodexProcessSessionFactory(
            async (line, session) =>
            {
                using var document = JsonDocument.Parse(line);
                var method = document.RootElement.TryGetProperty("method", out var methodElement)
                    ? methodElement.GetString()
                    : null;

                if (method == "initialize")
                {
                    session.EnqueueStdout(new { id = 1, result = new { } });
                    return;
                }

                if (method == "thread/start")
                {
                    session.EnqueueStdout(new { id = 2, result = new { thread = new { id = "thread-123" } } });
                    return;
                }

                if (method == "turn/start")
                {
                    session.EnqueueStdout(new { id = 3, result = new { turn = new { id = "turn-456" } } });
                    session.EnqueueStdout(new
                    {
                        method = "item/agentMessage/delta",
                        @params = new
                        {
                            itemId = "agent-1",
                            delta = "partial"
                        }
                    });
                    session.EnqueueStdout(new
                    {
                        method = "item/completed",
                        @params = new
                        {
                            item = new
                            {
                                id = "agent-1",
                                type = "agentMessage",
                                content = new object[]
                                {
                                    new
                                    {
                                        type = "output_text",
                                        text = "Applied a detailed remediation plan."
                                    }
                                }
                            }
                        }
                    });
                    session.EnqueueStdout(new
                    {
                        method = "turn/completed",
                        @params = new
                        {
                            message = "done",
                            usage = new
                            {
                                input_tokens = 30,
                                input_tokens_details = new
                                {
                                    cached_tokens = 6
                                },
                                output_tokens = 12,
                                output_tokens_details = new
                                {
                                    reasoning_tokens = 4
                                },
                                total_tokens = 42
                            }
                        }
                    });
                }

                await Task.CompletedTask;
            });
        var client = CreateClient(sessionFactory, transcriptSink);
        using var testContext = CreateContext();

        await client.RunAsync(testContext.Context, Path.GetTempPath(), "Prompt body", CreateCodexOptions());

        var latestSession = Assert.IsType<LiveSessionMetadata>(testContext.Context.Session);
        Assert.True(latestSession.EstimatedInputTokens > 0);
        Assert.True(latestSession.EstimatedOutputTokens > 0);
        Assert.Equal(30, latestSession.LastReportedInputTokens);
        Assert.Equal(6, latestSession.LastReportedCachedInputTokens);
        Assert.Equal(12, latestSession.LastReportedOutputTokens);
        Assert.Equal(4, latestSession.LastReportedReasoningTokens);
        Assert.Equal(42, latestSession.LastReportedTotalTokens);
        Assert.Equal(42, latestSession.CodexTotalTokens);
        Assert.Equal(SessionTokenComparisonStatus.Mismatch, latestSession.TokenComparisonStatus);
        Assert.NotNull(latestSession.LastUsageOperation);
        Assert.Equal("turn-456:turn_completed", latestSession.LastUsageOperation!.OperationId);
        Assert.Equal(6, latestSession.LastUsageOperation.CachedInputTokens);
        Assert.Equal(4, latestSession.LastUsageOperation.ReasoningTokens);

        Assert.NotEmpty(transcriptSink.SessionMetadata);
        var lastMetadata = transcriptSink.SessionMetadata[^1];
        Assert.Equal("#21", lastMetadata.IssueIdentifier);
        Assert.Equal(latestSession.SessionId, lastMetadata.Session.SessionId);
        Assert.Equal(42, lastMetadata.Session.CodexTotalTokens);
        Assert.True(lastMetadata.Session.LastEstimatedTokenAt.HasValue);
        Assert.True(lastMetadata.Session.LastReportedTokenAt.HasValue);
    }

    [Fact]
    public async Task RunAsync_accumulates_reported_usage_across_multiple_turns()
    {
        var transcriptSink = new RecordingTranscriptSink();
        var turnCounter = 0;
        var sessionFactory = new TestCodexProcessSessionFactory(
            async (line, session) =>
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var method = root.TryGetProperty("method", out var methodElement)
                    ? methodElement.GetString()
                    : null;

                if (method == "initialize")
                {
                    session.EnqueueStdout(new { id = 1, result = new { } });
                    return;
                }

                if (method == "thread/start")
                {
                    session.EnqueueStdout(new { id = 2, result = new { thread = new { id = "thread-123" } } });
                    return;
                }

                if (method == "turn/start")
                {
                    turnCounter++;
                    session.EnqueueStdout(new { id = turnCounter + 2, result = new { turn = new { id = $"turn-{turnCounter}" } } });
                    session.EnqueueStdout(new
                    {
                        method = "turn/completed",
                        @params = new
                        {
                            message = $"done-{turnCounter}",
                            usage = turnCounter == 1
                                ? new
                                {
                                    input_tokens = 30,
                                    input_tokens_details = new { cached_tokens = 6 },
                                    output_tokens = 12,
                                    output_tokens_details = new { reasoning_tokens = 4 },
                                    total_tokens = 42
                                }
                                : new
                                {
                                    input_tokens = 20,
                                    input_tokens_details = new { cached_tokens = 2 },
                                    output_tokens = 8,
                                    output_tokens_details = new { reasoning_tokens = 1 },
                                    total_tokens = 28
                                }
                        }
                    });
                }

                await Task.CompletedTask;
            });
        var client = CreateClient(sessionFactory, transcriptSink);
        using var testContext = CreateContext();

        await client.RunAsync(
            testContext.Context,
            Path.GetTempPath(),
            "Prompt body",
            CreateCodexOptions(),
            (completedTurnCount, _) => Task.FromResult<string?>(completedTurnCount == 1 ? "Continuation prompt" : null));

        var latestSession = Assert.IsType<LiveSessionMetadata>(testContext.Context.Session);
        Assert.Equal(50, latestSession.LastReportedInputTokens);
        Assert.Equal(8, latestSession.LastReportedCachedInputTokens);
        Assert.Equal(20, latestSession.LastReportedOutputTokens);
        Assert.Equal(5, latestSession.LastReportedReasoningTokens);
        Assert.Equal(70, latestSession.LastReportedTotalTokens);
        Assert.Equal(70, latestSession.CodexTotalTokens);
        Assert.Equal("turn-2:turn_completed", latestSession.LastUsageOperation!.OperationId);
    }

    private static WorkflowCodexOptions CreateCodexOptions()
    {
        return new WorkflowCodexOptions(
            "codex app-server",
            "never",
            "workspace-write",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = "workspaceWrite",
                ["networkAccess"] = true
            },
            60_000,
            5_000,
            300_000);
    }

    private static TestExecutionContext CreateContext()
    {
        var issue = new Issue(
            id: "21",
            identifier: "#21",
            title: "Implement Codex agent runner",
            description: "Runner story",
            state: "Todo");
        var logger = new RecordingLogger<ActiveSessionRegistry>();
        var registry = new ActiveSessionRegistry(TimeProvider.System, logger);
        var trackedSession = registry.BeginSession(issue, attempt: null, CancellationToken.None);

        return new TestExecutionContext(
            trackedSession,
            trackedSession.CreateExecutionContext(),
            logger);
    }

    private static CodexAppServerClient CreateClient(
        TestCodexProcessSessionFactory sessionFactory,
        RecordingTranscriptSink transcriptSink)
    {
        return new CodexAppServerClient(
            sessionFactory,
            TimeProvider.System,
            transcriptSink,
            NullLogger<CodexAppServerClient>.Instance);
    }

    private sealed class TestExecutionContext : IDisposable
    {
        private readonly ActiveSessionRegistry.TrackedActiveSession _trackedSession;

        public TestExecutionContext(
            ActiveSessionRegistry.TrackedActiveSession trackedSession,
            QueuedIssueExecutionContext context,
            RecordingLogger<ActiveSessionRegistry> logger)
        {
            _trackedSession = trackedSession;
            Context = context;
            Logger = logger;
        }

        public QueuedIssueExecutionContext Context { get; }

        public RecordingLogger<ActiveSessionRegistry> Logger { get; }

        public void Dispose()
        {
            _trackedSession.Dispose();
        }
    }

    private sealed class RecordingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var structuredState = state as IEnumerable<KeyValuePair<string, object?>>;
            var values = structuredState?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                ?? new Dictionary<string, object?>(StringComparer.Ordinal);

            Entries.Add(new LogEntry(formatter(state, exception), values));
        }
    }

    private sealed record LogEntry(string Message, IReadOnlyDictionary<string, object?> State);

    private sealed class RecordingTranscriptSink : IAgentDebugTranscriptSink
    {
        public RecordingTranscriptSink(bool trackAgentMessageDeltas = true)
        {
            TrackAgentMessageDeltas = trackAgentMessageDeltas;
        }

        public bool TrackAgentMessageDeltas { get; }

        public List<TranscriptEntry> Outbound { get; } = [];

        public List<TranscriptEntry> Inbound { get; } = [];

        public List<TranscriptEntry> Diagnostics { get; } = [];

        public List<SessionMetadataEntry> SessionMetadata { get; } = [];

        public void RecordOutbound(string issueIdentifier, DateTimeOffset timestamp, string title, string payload)
        {
            Outbound.Add(new TranscriptEntry(issueIdentifier, timestamp, title, payload));
        }

        public void RecordInbound(string issueIdentifier, DateTimeOffset timestamp, string title, string payload)
        {
            Inbound.Add(new TranscriptEntry(issueIdentifier, timestamp, title, payload));
        }

        public void RecordDiagnostic(string issueIdentifier, DateTimeOffset timestamp, string title, string detail)
        {
            Diagnostics.Add(new TranscriptEntry(issueIdentifier, timestamp, title, detail));
        }

        public void RecordSessionMetadata(
            string issueIdentifier,
            DateTimeOffset timestamp,
            LiveSessionMetadata session,
            int? attempt,
            string orchestratorSessionId)
        {
            SessionMetadata.Add(new SessionMetadataEntry(issueIdentifier, timestamp, session, attempt, orchestratorSessionId));
        }
    }

    private sealed record TranscriptEntry(string IssueIdentifier, DateTimeOffset Timestamp, string Title, string Payload);

    private sealed record SessionMetadataEntry(
        string IssueIdentifier,
        DateTimeOffset Timestamp,
        LiveSessionMetadata Session,
        int? Attempt,
        string OrchestratorSessionId);

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
