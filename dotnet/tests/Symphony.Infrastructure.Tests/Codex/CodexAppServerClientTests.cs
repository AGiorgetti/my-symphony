using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
        Assert.Contains(
            sessionFactory.Session!.SentLines,
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
    public async Task RunAsync_fails_when_codex_requests_user_input()
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

        var exception = await Assert.ThrowsAsync<CodexAgentException>(
            () => client.RunAsync(testContext.Context, Path.GetTempPath(), "Prompt body", CreateCodexOptions()));

        Assert.Equal("turn_input_required", exception.Code);
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

    private static WorkflowCodexOptions CreateCodexOptions()
    {
        return new WorkflowCodexOptions(
            "codex app-server",
            "never",
            "workspace-write",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = "workspaceWrite"
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
    }

    private sealed record TranscriptEntry(string IssueIdentifier, DateTimeOffset Timestamp, string Title, string Payload);

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
