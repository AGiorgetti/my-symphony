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
        var client = new CodexAppServerClient(sessionFactory, TimeProvider.System, NullLogger<CodexAppServerClient>.Instance);
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
    }

    [Fact]
    public async Task RunAsync_fails_when_codex_requests_user_input()
    {
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
        var client = new CodexAppServerClient(sessionFactory, TimeProvider.System, NullLogger<CodexAppServerClient>.Instance);
        using var testContext = CreateContext();

        var exception = await Assert.ThrowsAsync<CodexAgentException>(
            () => client.RunAsync(testContext.Context, Path.GetTempPath(), "Prompt body", CreateCodexOptions()));

        Assert.Equal("turn_input_required", exception.Code);
        Assert.NotNull(sessionFactory.Session);
        Assert.True(sessionFactory.Session!.WasKilled);
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

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
