using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Symphony.Application.Configuration;
using Symphony.Application.Orchestration;
using Symphony.Domain.Runs;
using Symphony.Domain.Sessions;

namespace Symphony.Infrastructure.Codex;

internal sealed class CodexAppServerClient(
    ICodexProcessSessionFactory processSessionFactory,
    TimeProvider timeProvider,
    ILogger<CodexAppServerClient> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task RunAsync(
        QueuedIssueExecutionContext context,
        string workspacePath,
        string prompt,
        WorkflowCodexOptions codexOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(codexOptions);

        await using var session = await processSessionFactory.StartAsync(
                new CodexProcessStartRequest(codexOptions.Command, workspacePath),
                context.CancellationToken)
            .ConfigureAwait(false);

        using var stderrCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        var standardErrorPump = PumpStandardErrorAsync(session, context, stderrCancellationTokenSource.Token);

        try
        {
            logger.LogInformation(
                "codex_launch started issue_id={issue_id} issue_identifier={issue_identifier} workspace_path={workspace_path} command={command} outcome=started",
                context.Issue.Id,
                context.Issue.Identifier,
                workspacePath,
                codexOptions.Command);

            await SendAsync(
                    session,
                    new
                    {
                        id = 1,
                        method = "initialize",
                        @params = new
                        {
                            clientInfo = new
                            {
                                name = "symphony",
                                version = typeof(CodexAppServerClient).Assembly.GetName().Version?.ToString() ?? "1.0.0"
                            },
                            capabilities = new { }
                        }
                    },
                    context.CancellationToken)
                .ConfigureAwait(false);
            _ = await ReadResponseAsync(session, 1, codexOptions.ReadTimeoutMs, context, context.CancellationToken).ConfigureAwait(false);

            await SendAsync(
                    session,
                    new
                    {
                        method = "initialized",
                        @params = new { }
                    },
                    context.CancellationToken)
                .ConfigureAwait(false);

            context.UpdateStatus(RunAttemptStatus.InitializingSession);

            await SendAsync(
                    session,
                    new
                    {
                        id = 2,
                        method = "thread/start",
                        @params = new
                        {
                            approvalPolicy = codexOptions.ApprovalPolicy,
                            sandbox = codexOptions.ThreadSandbox,
                            cwd = workspacePath
                        }
                    },
                    context.CancellationToken)
                .ConfigureAwait(false);
            var threadStartResponse = await ReadResponseAsync(session, 2, codexOptions.ReadTimeoutMs, context, context.CancellationToken)
                .ConfigureAwait(false);
            var threadId = ExtractRequiredNestedId(threadStartResponse, "thread");

            await SendAsync(
                    session,
                    new
                    {
                        id = 3,
                        method = "turn/start",
                        @params = new
                        {
                            threadId,
                            input = new object[]
                            {
                                new
                                {
                                    type = "text",
                                    text = prompt
                                }
                            },
                            cwd = workspacePath,
                            title = $"{context.Issue.Identifier}: {context.Issue.Title}",
                            approvalPolicy = codexOptions.ApprovalPolicy,
                            sandboxPolicy = codexOptions.TurnSandboxPolicy
                        }
                    },
                    context.CancellationToken)
                .ConfigureAwait(false);
            var turnStartResponse = await ReadResponseAsync(session, 3, codexOptions.ReadTimeoutMs, context, context.CancellationToken)
                .ConfigureAwait(false);
            var turnId = ExtractRequiredNestedId(turnStartResponse, "turn");

            context.UpdateSession(
                new LiveSessionMetadata(
                    threadId,
                    turnId,
                    codexAppServerPid: session.ProcessId?.ToString(CultureInfo.InvariantCulture),
                    lastCodexEvent: "session_started",
                    lastCodexTimestamp: timeProvider.GetUtcNow(),
                    lastCodexMessage: "turn_started",
                    turnCount: 1));
            context.UpdateStatus(RunAttemptStatus.StreamingTurn);

            await ReadTurnStreamAsync(session, context, codexOptions.TurnTimeoutMs).ConfigureAwait(false);

            logger.LogInformation(
                "codex_launch completed issue_id={issue_id} issue_identifier={issue_identifier} session_id={session_id} outcome=completed",
                context.Issue.Id,
                context.Issue.Identifier,
                context.SessionId);
        }
        finally
        {
            stderrCancellationTokenSource.Cancel();
            session.Kill();

            try
            {
                await session.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException)
            {
            }

            try
            {
                await standardErrorPump.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task<JsonElement> ReadResponseAsync(
        ICodexProcessSession session,
        int expectedId,
        int readTimeoutMs,
        QueuedIssueExecutionContext context,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            string? line;
            try
            {
                line = await session.ReadStandardOutputLineAsync(
                        TimeSpan.FromMilliseconds(readTimeoutMs),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException exception)
            {
                throw new CodexAgentException("response_timeout", "Timed out waiting for Codex app-server response.", exception);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new CodexAgentException("response_timeout", "Timed out waiting for Codex app-server response.", exception);
            }

            if (line is null)
            {
                throw new CodexAgentException(
                    "port_exit",
                    "Codex app-server exited before completing the startup handshake.");
            }

            JsonElement payload;
            try
            {
                using var document = JsonDocument.Parse(line);
                payload = document.RootElement.Clone();
            }
            catch (JsonException exception)
            {
                throw new CodexAgentException(
                    "response_error",
                    "Codex app-server emitted malformed JSON during the startup handshake.",
                    exception);
            }

            var responseId = TryGetId(payload);
            if (responseId is not null && string.Equals(responseId, expectedId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                if (payload.TryGetProperty("error", out var errorElement))
                {
                    throw new CodexAgentException("response_error", FormatErrorMessage(errorElement));
                }

                return payload;
            }

            await ProcessProtocolMessageAsync(session, context, payload, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReadTurnStreamAsync(
        ICodexProcessSession session,
        QueuedIssueExecutionContext context,
        int turnTimeoutMs)
    {
        using var turnTimeoutCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        turnTimeoutCancellationTokenSource.CancelAfter(TimeSpan.FromMilliseconds(turnTimeoutMs));

        try
        {
            while (true)
            {
                var line = await session.ReadStandardOutputLineAsync(null, turnTimeoutCancellationTokenSource.Token).ConfigureAwait(false);
                if (line is null)
                {
                    throw new CodexAgentException(
                        "port_exit",
                        "Codex app-server exited before the turn reached a terminal event.");
                }

                JsonElement payload;
                try
                {
                    using var document = JsonDocument.Parse(line);
                    payload = document.RootElement.Clone();
                }
                catch (JsonException)
                {
                    UpdateSessionMetadata(context, "malformed", "malformed_protocol_message", default);
                    logger.LogWarning(
                        "codex_protocol malformed issue_id={issue_id} issue_identifier={issue_identifier} session_id={session_id} outcome=failed",
                        context.Issue.Id,
                        context.Issue.Identifier,
                        context.SessionId);
                    continue;
                }

                var terminalOutcome = await ProcessProtocolMessageAsync(session, context, payload, turnTimeoutCancellationTokenSource.Token)
                    .ConfigureAwait(false);
                if (terminalOutcome == CodexTerminalOutcome.Completed)
                {
                    return;
                }

                if (terminalOutcome == CodexTerminalOutcome.Failed)
                {
                    throw new CodexAgentException("turn_failed", "Codex app-server reported a failed turn.");
                }

                if (terminalOutcome == CodexTerminalOutcome.Canceled)
                {
                    throw new CodexAgentException("turn_cancelled", "Codex app-server reported a cancelled turn.");
                }
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new CodexAgentException("turn_timeout", "Codex app-server turn timed out.", exception);
        }
    }

    private async Task PumpStandardErrorAsync(
        ICodexProcessSession session,
        QueuedIssueExecutionContext context,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await session.ReadStandardErrorLineAsync(null, cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                return;
            }

            logger.LogInformation(
                "codex_stderr completed issue_id={issue_id} issue_identifier={issue_identifier} session_id={session_id} diagnostic={diagnostic} outcome=completed",
                context.Issue.Id,
                context.Issue.Identifier,
                context.SessionId,
                line);
        }
    }

    private async Task<CodexTerminalOutcome> ProcessProtocolMessageAsync(
        ICodexProcessSession session,
        QueuedIssueExecutionContext context,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var method = TryGetMethod(payload);
        if (method is null)
        {
            return CodexTerminalOutcome.None;
        }

        if (TryGetId(payload) is { } requestId)
        {
            if (method.Contains("requestUserInput", StringComparison.OrdinalIgnoreCase)
                || method.Contains("user_input", StringComparison.OrdinalIgnoreCase)
                || method.Contains("input_required", StringComparison.OrdinalIgnoreCase))
            {
                UpdateSessionMetadata(context, "turn_input_required", "user_input_required", payload);
                throw new CodexAgentException("turn_input_required", "Codex app-server requested user input.");
            }

            if (method.Contains("approval", StringComparison.OrdinalIgnoreCase))
            {
                await SendAsync(
                        session,
                        new
                        {
                            id = requestId,
                            result = new
                            {
                                approved = true
                            }
                        },
                        cancellationToken)
                    .ConfigureAwait(false);

                UpdateSessionMetadata(context, "approval_auto_approved", "approval_auto_approved", payload);
                return CodexTerminalOutcome.None;
            }

            if (method.Contains("tool/call", StringComparison.OrdinalIgnoreCase)
                || method.Contains("tool_call", StringComparison.OrdinalIgnoreCase))
            {
                await SendAsync(
                        session,
                        new
                        {
                            id = requestId,
                            result = new
                            {
                                success = false,
                                error = "unsupported_tool_call"
                            }
                        },
                        cancellationToken)
                    .ConfigureAwait(false);

                UpdateSessionMetadata(context, "unsupported_tool_call", "unsupported_tool_call", payload);
                return CodexTerminalOutcome.None;
            }
        }

        return method switch
        {
            "turn/completed" => CompleteTerminalEvent(context, "turn_completed", payload, CodexTerminalOutcome.Completed),
            "turn/failed" => CompleteTerminalEvent(context, "turn_failed", payload, CodexTerminalOutcome.Failed),
            "turn/cancelled" => CompleteTerminalEvent(context, "turn_cancelled", payload, CodexTerminalOutcome.Canceled),
            _ => ObserveEvent(context, method.Replace('/', '_'), payload)
        };
    }

    private static CodexTerminalOutcome CompleteTerminalEvent(
        QueuedIssueExecutionContext context,
        string eventName,
        JsonElement payload,
        CodexTerminalOutcome outcome)
    {
        UpdateSessionMetadata(context, eventName, null, payload);
        return outcome;
    }

    private static CodexTerminalOutcome ObserveEvent(
        QueuedIssueExecutionContext context,
        string eventName,
        JsonElement payload)
    {
        UpdateSessionMetadata(context, eventName, null, payload);
        return CodexTerminalOutcome.None;
    }

    private static void UpdateSessionMetadata(
        QueuedIssueExecutionContext context,
        string eventName,
        string? fallbackMessage,
        JsonElement payload)
    {
        if (context.Session is null)
        {
            return;
        }

        var usage = ExtractUsage(payload);
        var lastReportedInputTokens = usage.InputTokens ?? context.Session.LastReportedInputTokens;
        var lastReportedOutputTokens = usage.OutputTokens ?? context.Session.LastReportedOutputTokens;
        var lastReportedTotalTokens = usage.TotalTokens
            ?? Math.Max(context.Session.LastReportedTotalTokens, lastReportedInputTokens + lastReportedOutputTokens);
        var codexInputTokens = Math.Max(context.Session.CodexInputTokens, lastReportedInputTokens);
        var codexOutputTokens = Math.Max(context.Session.CodexOutputTokens, lastReportedOutputTokens);
        var codexTotalTokens = Math.Max(
            Math.Max(context.Session.CodexTotalTokens, lastReportedTotalTokens),
            codexInputTokens + codexOutputTokens);

        context.UpdateSession(
            new LiveSessionMetadata(
                context.Session.ThreadId,
                context.Session.TurnId,
                context.Session.CodexAppServerPid,
                eventName,
                DateTimeOffset.UtcNow,
                ExtractMessage(payload) ?? fallbackMessage ?? eventName,
                codexInputTokens,
                codexOutputTokens,
                codexTotalTokens,
                lastReportedInputTokens,
                lastReportedOutputTokens,
                lastReportedTotalTokens,
                context.Session.TurnCount));
    }

    private static async Task SendAsync(
        ICodexProcessSession session,
        object payload,
        CancellationToken cancellationToken)
    {
        var line = JsonSerializer.Serialize(payload, SerializerOptions);
        await session.SendAsync(line, cancellationToken).ConfigureAwait(false);
    }

    private static string ExtractRequiredNestedId(JsonElement payload, string propertyName)
    {
        if (payload.TryGetProperty("result", out var result)
            && result.ValueKind == JsonValueKind.Object)
        {
            if (result.TryGetProperty(propertyName, out var nested)
                && nested.ValueKind == JsonValueKind.Object
                && nested.TryGetProperty("id", out var idElement))
            {
                return GetRequiredString(idElement, $"result.{propertyName}.id");
            }

            if (result.TryGetProperty($"{propertyName}Id", out var camelCaseId))
            {
                return GetRequiredString(camelCaseId, $"result.{propertyName}Id");
            }

            if (result.TryGetProperty($"{propertyName}_id", out var snakeCaseId))
            {
                return GetRequiredString(snakeCaseId, $"result.{propertyName}_id");
            }
        }

        throw new CodexAgentException("response_error", $"Codex app-server response was missing result.{propertyName}.id.");
    }

    private static string GetRequiredString(JsonElement element, string fieldName)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String when !string.IsNullOrWhiteSpace(element.GetString()) => element.GetString()!,
            JsonValueKind.Number => element.GetRawText(),
            _ => throw new CodexAgentException("response_error", $"Codex app-server response field '{fieldName}' was not a string.")
        };
    }

    private static string? TryGetMethod(JsonElement payload)
    {
        return payload.TryGetProperty("method", out var methodElement)
            && methodElement.ValueKind == JsonValueKind.String
            ? methodElement.GetString()
            : null;
    }

    private static string? TryGetId(JsonElement payload)
    {
        if (!payload.TryGetProperty("id", out var idElement))
        {
            return null;
        }

        return idElement.ValueKind switch
        {
            JsonValueKind.String => idElement.GetString(),
            JsonValueKind.Number => idElement.GetRawText(),
            _ => null
        };
    }

    private static string FormatErrorMessage(JsonElement errorElement)
    {
        return ExtractMessage(errorElement) ?? errorElement.GetRawText();
    }

    private static string? ExtractMessage(JsonElement payload)
    {
        return FindFirstString(
            payload,
            static name => name is "message" or "text" or "reason" or "error");
    }

    private static UsageInfo ExtractUsage(JsonElement payload)
    {
        return new UsageInfo(
            FindFirstInt(payload, static name => name is "input_tokens" or "inputTokens"),
            FindFirstInt(payload, static name => name is "output_tokens" or "outputTokens"),
            FindFirstInt(payload, static name => name is "total_tokens" or "totalTokens"));
    }

    private static int? FindFirstInt(JsonElement element, Func<string, bool> nameMatcher)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => FindFirstIntInObject(element, nameMatcher),
            JsonValueKind.Array => FindFirstIntInArray(element, nameMatcher),
            _ => null
        };
    }

    private static int? FindFirstIntInObject(JsonElement element, Func<string, bool> nameMatcher)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (nameMatcher(property.Name) && property.Value.TryGetInt32(out var intValue))
            {
                return intValue;
            }

            var nested = FindFirstInt(property.Value, nameMatcher);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static int? FindFirstIntInArray(JsonElement element, Func<string, bool> nameMatcher)
    {
        foreach (var item in element.EnumerateArray())
        {
            var nested = FindFirstInt(item, nameMatcher);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static string? FindFirstString(JsonElement element, Func<string, bool> nameMatcher)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => FindFirstStringInObject(element, nameMatcher),
            JsonValueKind.Array => FindFirstStringInArray(element, nameMatcher),
            _ => null
        };
    }

    private static string? FindFirstStringInObject(JsonElement element, Func<string, bool> nameMatcher)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (nameMatcher(property.Name) && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }

            var nested = FindFirstString(property.Value, nameMatcher);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static string? FindFirstStringInArray(JsonElement element, Func<string, bool> nameMatcher)
    {
        foreach (var item in element.EnumerateArray())
        {
            var nested = FindFirstString(item, nameMatcher);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private readonly record struct UsageInfo(int? InputTokens, int? OutputTokens, int? TotalTokens);

    private enum CodexTerminalOutcome
    {
        None,
        Completed,
        Failed,
        Canceled
    }
}

internal interface ICodexProcessSessionFactory
{
    Task<ICodexProcessSession> StartAsync(CodexProcessStartRequest request, CancellationToken cancellationToken);
}

internal readonly record struct CodexProcessStartRequest(string Command, string WorkingDirectory);

internal interface ICodexProcessSession : IAsyncDisposable
{
    int? ProcessId { get; }

    int? ExitCode { get; }

    bool HasExited { get; }

    Task SendAsync(string line, CancellationToken cancellationToken);

    Task<string?> ReadStandardOutputLineAsync(TimeSpan? timeout, CancellationToken cancellationToken);

    Task<string?> ReadStandardErrorLineAsync(TimeSpan? timeout, CancellationToken cancellationToken);

    Task WaitForExitAsync(CancellationToken cancellationToken);

    void Kill();
}

internal sealed class ProcessCodexProcessSessionFactory : ICodexProcessSessionFactory
{
    public Task<ICodexProcessSession> StartAsync(CodexProcessStartRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Command);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkingDirectory);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "bash",
                WorkingDirectory = request.WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("-lc");
            startInfo.ArgumentList.Add(request.Command);

            var process = new Process
            {
                StartInfo = startInfo
            };

            if (!process.Start())
            {
                throw new CodexAgentException("codex_not_found", $"Failed to launch Codex command '{request.Command}'.");
            }

            return Task.FromResult<ICodexProcessSession>(new ProcessCodexProcessSession(process));
        }
        catch (CodexAgentException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new CodexAgentException(
                "codex_not_found",
                $"Failed to launch Codex command '{request.Command}'. {exception.Message}",
                exception);
        }
    }

    private sealed class ProcessCodexProcessSession(Process process) : ICodexProcessSession
    {
        private int _disposed;

        public int? ProcessId => process.HasExited ? null : process.Id;

        public int? ExitCode => process.HasExited ? process.ExitCode : null;

        public bool HasExited => process.HasExited;

        public async Task SendAsync(string line, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await process.StandardInput.WriteLineAsync(line).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public Task<string?> ReadStandardOutputLineAsync(TimeSpan? timeout, CancellationToken cancellationToken)
        {
            return ReadLineAsync(process.StandardOutput, timeout, cancellationToken);
        }

        public Task<string?> ReadStandardErrorLineAsync(TimeSpan? timeout, CancellationToken cancellationToken)
        {
            return ReadLineAsync(process.StandardError, timeout, cancellationToken);
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            return process.WaitForExitAsync(cancellationToken);
        }

        public void Kill()
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Kill();

            try
            {
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
            }

            process.Dispose();
        }

        private static Task<string?> ReadLineAsync(
            StreamReader reader,
            TimeSpan? timeout,
            CancellationToken cancellationToken)
        {
            var readTask = reader.ReadLineAsync();
            return timeout is null
                ? readTask.WaitAsync(cancellationToken)
                : readTask.WaitAsync(timeout.Value, cancellationToken);
        }
    }
}

internal sealed class CodexAgentException(string code, string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException)
{
    public string Code { get; } = code;
}
