using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Symphony.Abstractions.Orchestration;
using Symphony.Application.Configuration;
using Symphony.Application.Orchestration;
using Symphony.Domain.Runs;
using Symphony.Domain.Sessions;

namespace Symphony.Infrastructure.Codex;

internal sealed class CodexAppServerClient(
    ICodexProcessSessionFactory processSessionFactory,
    TimeProvider timeProvider,
    IAgentDebugTranscriptSink debugTranscriptSink,
    ILogger<CodexAppServerClient> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task RunAsync(
        QueuedIssueExecutionContext context,
        string workspacePath,
        string prompt,
        WorkflowCodexOptions codexOptions,
        Func<int, CancellationToken, Task<string?>>? continuationPromptFactory = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(codexOptions);

        await using var session = await processSessionFactory.StartAsync(
                new CodexProcessStartRequest(codexOptions.Command, workspacePath),
                context.CancellationToken)
            .ConfigureAwait(false);

        var startupDiagnostics = new ConcurrentQueue<string>();
        using var stderrCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        var standardErrorPump = PumpStandardErrorAsync(session, context, startupDiagnostics, stderrCancellationTokenSource.Token);

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
                    context,
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
            _ = await ReadResponseAsync(session, 1, codexOptions.ReadTimeoutMs, context, startupDiagnostics, codexOptions.ApprovalPolicy, context.CancellationToken).ConfigureAwait(false);

            await SendAsync(
                    session,
                    context,
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
                    context,
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
            var threadStartResponse = await ReadResponseAsync(session, 2, codexOptions.ReadTimeoutMs, context, startupDiagnostics, codexOptions.ApprovalPolicy, context.CancellationToken)
                .ConfigureAwait(false);
            var threadId = ExtractRequiredNestedId(threadStartResponse, "thread");

            var title = $"{context.Issue.Identifier}: {context.Issue.Title}";
            var completedTurnCount = 0;
            var nextPrompt = prompt;

            while (true)
            {
                completedTurnCount++;
                var turnStartRequestId = completedTurnCount + 2;
                await RunTurnAsync(
                        session,
                        context,
                        workspacePath,
                        title,
                        threadId,
                        nextPrompt,
                        turnStartRequestId,
                        completedTurnCount,
                        codexOptions,
                        startupDiagnostics)
                    .ConfigureAwait(false);

                if (continuationPromptFactory is null)
                {
                    break;
                }

                nextPrompt = await continuationPromptFactory(completedTurnCount, context.CancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(nextPrompt))
                {
                    break;
                }
            }

            logger.LogInformation(
                "codex_launch completed issue_id={issue_id} issue_identifier={issue_identifier} session_id={session_id} outcome=completed",
                context.Issue.Id,
                context.Issue.Identifier,
                context.SessionId);
        }
        catch (CodexAgentException exception) when (exception.Code == "port_exit")
        {
            await AwaitStandardErrorPumpAsync(standardErrorPump).ConfigureAwait(false);

            if (exception.Message.Contains("startup handshake", StringComparison.Ordinal)
                && !exception.Message.Contains("stderr=", StringComparison.Ordinal))
            {
                throw new CodexAgentException(
                    exception.Code,
                    BuildStartupHandshakeExitMessage(session, startupDiagnostics),
                    exception);
            }

            throw;
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

    private static async Task AwaitStandardErrorPumpAsync(Task standardErrorPump)
    {
        try
        {
            await standardErrorPump.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunTurnAsync(
        ICodexProcessSession session,
        QueuedIssueExecutionContext context,
        string workspacePath,
        string title,
        string threadId,
        string prompt,
        int requestId,
        int turnNumber,
        WorkflowCodexOptions codexOptions,
        ConcurrentQueue<string> startupDiagnostics)
    {
        var estimatedInputTokenDelta = EstimateTextTokens(prompt);
        var previousSession = context.Session;

        await SendAsync(
                session,
                context,
                new
                {
                    id = requestId,
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
                        title,
                        approvalPolicy = codexOptions.ApprovalPolicy,
                        sandboxPolicy = codexOptions.TurnSandboxPolicy
                    }
                },
                context.CancellationToken)
            .ConfigureAwait(false);
        var turnStartResponse = await ReadResponseAsync(
                session,
                requestId,
                codexOptions.ReadTimeoutMs,
                context,
                startupDiagnostics,
                codexOptions.ApprovalPolicy,
                context.CancellationToken)
            .ConfigureAwait(false);
        var turnId = ExtractRequiredNestedId(turnStartResponse, "turn");
        var sessionUpdatedAt = timeProvider.GetUtcNow();
        var estimatedInputTokens = (previousSession?.EstimatedInputTokens ?? 0) + estimatedInputTokenDelta;
        var estimatedOutputTokens = previousSession?.EstimatedOutputTokens ?? 0;
        var estimatedTotalTokens = Math.Max(previousSession?.EstimatedTotalTokens ?? 0, estimatedInputTokens + estimatedOutputTokens);
        var lastReportedInputTokens = previousSession?.LastReportedInputTokens ?? 0;
        var lastReportedOutputTokens = previousSession?.LastReportedOutputTokens ?? 0;
        var lastReportedTotalTokens = previousSession?.LastReportedTotalTokens ?? 0;
        var comparison = CompareTokenUsage(
            estimatedInputTokens,
            estimatedOutputTokens,
            estimatedTotalTokens,
            lastReportedInputTokens,
            lastReportedOutputTokens,
            lastReportedTotalTokens);
        var effectiveTotals = ResolveEffectiveTokenUsage(
            previousSession?.CodexInputTokens ?? 0,
            previousSession?.CodexOutputTokens ?? 0,
            previousSession?.CodexTotalTokens ?? 0,
            estimatedInputTokens,
            estimatedOutputTokens,
            estimatedTotalTokens,
            lastReportedInputTokens,
            lastReportedOutputTokens,
            lastReportedTotalTokens);

        var liveSession = new LiveSessionMetadata(
            threadId,
            turnId,
            codexAppServerPid: session.ProcessId?.ToString(CultureInfo.InvariantCulture),
            lastCodexEvent: turnNumber == 1 ? "session_started" : "turn_started",
            lastCodexTimestamp: sessionUpdatedAt,
            lastCodexMessage: "turn_started",
            codexInputTokens: effectiveTotals.InputTokens,
            codexOutputTokens: effectiveTotals.OutputTokens,
            codexTotalTokens: effectiveTotals.TotalTokens,
            estimatedInputTokens: estimatedInputTokens,
            estimatedOutputTokens: estimatedOutputTokens,
            estimatedTotalTokens: estimatedTotalTokens,
            lastReportedInputTokens: lastReportedInputTokens,
            lastReportedCachedInputTokens: previousSession?.LastReportedCachedInputTokens ?? 0,
            lastReportedOutputTokens: lastReportedOutputTokens,
            lastReportedReasoningTokens: previousSession?.LastReportedReasoningTokens ?? 0,
            lastReportedTotalTokens: lastReportedTotalTokens,
            tokenComparisonStatus: comparison.Status,
            tokenInputDelta: comparison.InputDelta,
            tokenOutputDelta: comparison.OutputDelta,
            tokenTotalDelta: comparison.TotalDelta,
            lastEstimatedTokenAt: estimatedInputTokenDelta > 0 ? sessionUpdatedAt : previousSession?.LastEstimatedTokenAt,
            lastReportedTokenAt: previousSession?.LastReportedTokenAt,
            lastUsageOperation: previousSession?.LastUsageOperation,
            turnCount: turnNumber);

        context.UpdateSession(liveSession);
        RecordSessionMetadata(context, sessionUpdatedAt, liveSession);
        context.UpdateStatus(RunAttemptStatus.StreamingTurn);

        await ReadTurnStreamAsync(session, context, codexOptions.TurnTimeoutMs, codexOptions.ApprovalPolicy).ConfigureAwait(false);
    }

    private async Task<JsonElement> ReadResponseAsync(
        ICodexProcessSession session,
        int expectedId,
        int readTimeoutMs,
        QueuedIssueExecutionContext context,
        ConcurrentQueue<string> startupDiagnostics,
        string? approvalPolicy,
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
                    BuildStartupHandshakeExitMessage(session, startupDiagnostics));
            }

            JsonElement payload;
            try
            {
                using var document = JsonDocument.Parse(line);
                payload = document.RootElement.Clone();
            }
            catch (JsonException exception)
            {
                RecordDiagnostic(context, "Received malformed startup payload", line);
                throw new CodexAgentException(
                    "response_error",
                    "Codex app-server emitted malformed JSON during the startup handshake.",
                    exception);
            }

            RecordInboundPayload(context, payload, line);

            var responseId = TryGetId(payload);
            if (responseId is not null && string.Equals(responseId, expectedId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                if (payload.TryGetProperty("error", out var errorElement))
                {
                    throw new CodexAgentException("response_error", FormatErrorMessage(errorElement));
                }

                return payload;
            }

            await ProcessProtocolMessageAsync(session, context, payload, approvalPolicy, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReadTurnStreamAsync(
        ICodexProcessSession session,
        QueuedIssueExecutionContext context,
        int turnTimeoutMs,
        string? approvalPolicy)
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
                    RecordDiagnostic(context, "Received malformed turn payload", line);
                    UpdateSessionMetadata(context, "malformed", "malformed_protocol_message", default);
                    logger.LogWarning(
                        "codex_protocol malformed issue_id={issue_id} issue_identifier={issue_identifier} session_id={session_id} outcome=failed",
                        context.Issue.Id,
                        context.Issue.Identifier,
                        context.SessionId);
                    continue;
                }

                RecordInboundPayload(context, payload, line);

                var terminalOutcome = await ProcessProtocolMessageAsync(session, context, payload, approvalPolicy, turnTimeoutCancellationTokenSource.Token)
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
        ConcurrentQueue<string> startupDiagnostics,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await session.ReadStandardErrorLineAsync(null, cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                return;
            }

            RecordStartupDiagnostic(startupDiagnostics, line);
            RecordDiagnostic(context, "Received stderr", line);

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
        string? approvalPolicy,
        CancellationToken cancellationToken)
    {
        var method = TryGetMethod(payload);
        if (method is null)
        {
            return CodexTerminalOutcome.None;
        }

        if (TryGetResponseId(payload, out var requestId))
        {
            if (IsToolRequestUserInputMethod(method)
                && string.Equals(approvalPolicy, "never", StringComparison.OrdinalIgnoreCase)
                && TryCreateToolRequestUserInputApprovalResponse(payload, out var toolRequestApprovalResponse))
            {
                await SendAsync(
                        session,
                        context,
                        new
                        {
                            id = requestId,
                            result = toolRequestApprovalResponse
                        },
                        cancellationToken)
                    .ConfigureAwait(false);

                UpdateSessionMetadata(context, "tool_request_user_input_auto_approved", "tool_request_user_input_auto_approved", payload);
                return CodexTerminalOutcome.None;
            }

            if (IsMcpServerElicitationRequestMethod(method)
                && string.Equals(approvalPolicy, "never", StringComparison.OrdinalIgnoreCase)
                && TryCreateMcpToolCallElicitationApprovalResponse(payload, out var elicitationApprovalResponse))
            {
                await SendAsync(
                        session,
                        context,
                        new
                        {
                            id = requestId,
                            result = elicitationApprovalResponse
                        },
                        cancellationToken)
                    .ConfigureAwait(false);

                UpdateSessionMetadata(context, "mcp_tool_call_elicitation_auto_approved", "mcp_tool_call_elicitation_auto_approved", payload);
                return CodexTerminalOutcome.None;
            }

            if (method.Contains("requestUserInput", StringComparison.OrdinalIgnoreCase)
                || method.Contains("user_input", StringComparison.OrdinalIgnoreCase)
                || method.Contains("input_required", StringComparison.OrdinalIgnoreCase)
                || IsMcpServerElicitationRequestMethod(method))
            {
                UpdateSessionMetadata(context, "turn_input_required", "user_input_required", payload);
                throw CreateBlockingException(method, payload);
            }

            if (method.Contains("approval", StringComparison.OrdinalIgnoreCase))
            {
                await SendAsync(
                        session,
                        context,
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
                        context,
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

    private CodexTerminalOutcome CompleteTerminalEvent(
        QueuedIssueExecutionContext context,
        string eventName,
        JsonElement payload,
        CodexTerminalOutcome outcome)
    {
        UpdateSessionMetadata(context, eventName, null, payload);
        return outcome;
    }

    private CodexTerminalOutcome ObserveEvent(
        QueuedIssueExecutionContext context,
        string eventName,
        JsonElement payload)
    {
        UpdateSessionMetadata(context, eventName, null, payload);
        return CodexTerminalOutcome.None;
    }

    private void UpdateSessionMetadata(
        QueuedIssueExecutionContext context,
        string eventName,
        string? fallbackMessage,
        JsonElement payload)
    {
        if (context.Session is null)
        {
            return;
        }

        var timestamp = timeProvider.GetUtcNow();
        var usage = ExtractUsage(payload);
        var outputEstimateDelta = EstimateAssistantOutputTokens(payload);
        var estimatedInputTokens = context.Session.EstimatedInputTokens;
        var estimatedOutputTokens = context.Session.EstimatedOutputTokens + outputEstimateDelta;
        var estimatedTotalTokens = Math.Max(
            context.Session.EstimatedTotalTokens,
            estimatedInputTokens + estimatedOutputTokens);
        var lastReportedInputTokens = context.Session.LastReportedInputTokens;
        var lastReportedCachedInputTokens = context.Session.LastReportedCachedInputTokens;
        var lastReportedOutputTokens = context.Session.LastReportedOutputTokens;
        var lastReportedReasoningTokens = context.Session.LastReportedReasoningTokens;
        var lastReportedTotalTokens = context.Session.LastReportedTotalTokens;
        SessionTokenUsageOperation? lastUsageOperation = context.Session.LastUsageOperation;
        if (TryCreateReportedUsageOperation(context.Session, eventName, timestamp, usage, out var usageOperation))
        {
            var reportedOperation = usageOperation!;
            lastReportedInputTokens += reportedOperation.InputTokens;
            lastReportedCachedInputTokens += reportedOperation.CachedInputTokens;
            lastReportedOutputTokens += reportedOperation.OutputTokens;
            lastReportedReasoningTokens += reportedOperation.ReasoningTokens;
            lastReportedTotalTokens += reportedOperation.TotalTokens;
            lastUsageOperation = reportedOperation;
        }

        var comparison = CompareTokenUsage(
            estimatedInputTokens,
            estimatedOutputTokens,
            estimatedTotalTokens,
            lastReportedInputTokens,
            lastReportedOutputTokens,
            lastReportedTotalTokens);
        var effectiveTotals = usageOperation is null
            ? ResolveEffectiveTokenUsage(
                context.Session.CodexInputTokens,
                context.Session.CodexOutputTokens,
                context.Session.CodexTotalTokens,
                estimatedInputTokens,
                estimatedOutputTokens,
                estimatedTotalTokens,
                lastReportedInputTokens,
                lastReportedOutputTokens,
                lastReportedTotalTokens)
            : new TokenTotals(
                lastReportedInputTokens,
                lastReportedOutputTokens,
                lastReportedTotalTokens);

        var updatedSession = new LiveSessionMetadata(
            context.Session.ThreadId,
            context.Session.TurnId,
            context.Session.CodexAppServerPid,
            eventName,
            timestamp,
            ExtractMessage(payload) ?? fallbackMessage ?? eventName,
            effectiveTotals.InputTokens,
            effectiveTotals.OutputTokens,
            effectiveTotals.TotalTokens,
            estimatedInputTokens,
            estimatedOutputTokens,
            estimatedTotalTokens,
            lastReportedInputTokens,
            lastReportedCachedInputTokens,
            lastReportedOutputTokens,
            lastReportedReasoningTokens,
            lastReportedTotalTokens,
            comparison.Status,
            comparison.InputDelta,
            comparison.OutputDelta,
            comparison.TotalDelta,
            outputEstimateDelta > 0 ? timestamp : context.Session.LastEstimatedTokenAt,
            usageOperation is null ? context.Session.LastReportedTokenAt : timestamp,
            lastUsageOperation,
            context.Session.TurnCount);

        context.UpdateSession(updatedSession);
        RecordSessionMetadata(context, timestamp, updatedSession);
    }

    private async Task SendAsync(
        ICodexProcessSession session,
        QueuedIssueExecutionContext context,
        object payload,
        CancellationToken cancellationToken)
    {
        var line = JsonSerializer.Serialize(payload, SerializerOptions);
        RecordOutboundPayload(context, line);
        await session.SendAsync(line, cancellationToken).ConfigureAwait(false);
    }

    private void RecordOutboundPayload(QueuedIssueExecutionContext context, string line)
    {
        debugTranscriptSink.RecordOutbound(
            context.Issue.Identifier,
            timeProvider.GetUtcNow(),
            CreateOutboundTranscriptTitle(line),
            line);
    }

    private void RecordInboundPayload(QueuedIssueExecutionContext context, JsonElement payload, string line)
    {
        if (IsAgentMessageDelta(payload) && !debugTranscriptSink.TrackAgentMessageDeltas)
        {
            return;
        }

        debugTranscriptSink.RecordInbound(
            context.Issue.Identifier,
            timeProvider.GetUtcNow(),
            CreateInboundTranscriptTitle(payload),
            line);
    }

    private void RecordDiagnostic(QueuedIssueExecutionContext context, string title, string detail)
    {
        debugTranscriptSink.RecordDiagnostic(
            context.Issue.Identifier,
            timeProvider.GetUtcNow(),
            title,
            detail);
    }

    private void RecordSessionMetadata(
        QueuedIssueExecutionContext context,
        DateTimeOffset timestamp,
        LiveSessionMetadata session)
    {
        debugTranscriptSink.RecordSessionMetadata(
            context.Issue.Identifier,
            timestamp,
            session,
            context.Attempt,
            context.OrchestratorSessionId);
    }

    private static int EstimateTextTokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var trimmed = text.Trim();
        return Math.Max(1, (int)Math.Ceiling(trimmed.Length / 4d));
    }

    private static int EstimateAssistantOutputTokens(JsonElement payload)
    {
        var method = TryGetMethod(payload);
        if (!string.Equals(method, "item/completed", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (!payload.TryGetProperty("params", out var parameters)
            || parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("item", out var item)
            || item.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        var total = 0;
        if (item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var contentItem in content.EnumerateArray())
            {
                if (contentItem.ValueKind == JsonValueKind.Object
                    && contentItem.TryGetProperty("text", out var textElement)
                    && textElement.ValueKind == JsonValueKind.String)
                {
                    total += EstimateTextTokens(textElement.GetString());
                }
            }
        }

        return total;
    }

    private static bool TryCreateReportedUsageOperation(
        LiveSessionMetadata session,
        string eventName,
        DateTimeOffset timestamp,
        UsageInfo usage,
        out SessionTokenUsageOperation? operation)
    {
        operation = null;

        if (!usage.HasAny || !IsUsageAggregationEvent(eventName))
        {
            return false;
        }

        var inputTokens = usage.InputTokens ?? 0;
        var cachedInputTokens = usage.CachedInputTokens ?? 0;
        var outputTokens = usage.OutputTokens ?? 0;
        var reasoningTokens = usage.ReasoningTokens ?? 0;
        var totalTokens = usage.TotalTokens ?? Math.Max(inputTokens + outputTokens, 0);
        var operationId = $"{session.TurnId}:{eventName}";

        if (string.Equals(session.LastUsageOperation?.OperationId, operationId, StringComparison.Ordinal))
        {
            return false;
        }

        operation = new SessionTokenUsageOperation(
            operationId,
            eventName,
            timestamp,
            session.TurnCount,
            inputTokens,
            cachedInputTokens,
            outputTokens,
            reasoningTokens,
            totalTokens);
        return true;
    }

    private static bool IsUsageAggregationEvent(string eventName)
    {
        return eventName is "turn_completed" or "turn_failed" or "turn_cancelled";
    }

    private static TokenComparisonInfo CompareTokenUsage(
        int estimatedInputTokens,
        int estimatedOutputTokens,
        int estimatedTotalTokens,
        int reportedInputTokens,
        int reportedOutputTokens,
        int reportedTotalTokens)
    {
        var hasEstimate = estimatedInputTokens > 0 || estimatedOutputTokens > 0 || estimatedTotalTokens > 0;
        var hasReport = reportedInputTokens > 0 || reportedOutputTokens > 0 || reportedTotalTokens > 0;
        if (hasEstimate && hasReport)
        {
            var inputDelta = reportedInputTokens - estimatedInputTokens;
            var outputDelta = reportedOutputTokens - estimatedOutputTokens;
            var totalDelta = reportedTotalTokens - estimatedTotalTokens;
            var status = inputDelta == 0 && outputDelta == 0 && totalDelta == 0
                ? SessionTokenComparisonStatus.Match
                : SessionTokenComparisonStatus.Mismatch;
            return new TokenComparisonInfo(status, inputDelta, outputDelta, totalDelta);
        }

        if (hasEstimate)
        {
            return new TokenComparisonInfo(SessionTokenComparisonStatus.EstimatedOnly, 0, 0, 0);
        }

        if (hasReport)
        {
            return new TokenComparisonInfo(SessionTokenComparisonStatus.ReportedOnly, 0, 0, 0);
        }

        return new TokenComparisonInfo(SessionTokenComparisonStatus.None, 0, 0, 0);
    }

    private static TokenTotals ResolveEffectiveTokenUsage(
        int currentInputTokens,
        int currentOutputTokens,
        int currentTotalTokens,
        int estimatedInputTokens,
        int estimatedOutputTokens,
        int estimatedTotalTokens,
        int reportedInputTokens,
        int reportedOutputTokens,
        int reportedTotalTokens)
    {
        var inputTokens = Math.Max(currentInputTokens, Math.Max(estimatedInputTokens, reportedInputTokens));
        var outputTokens = Math.Max(currentOutputTokens, Math.Max(estimatedOutputTokens, reportedOutputTokens));
        var totalTokens = Math.Max(
            Math.Max(currentTotalTokens, Math.Max(estimatedTotalTokens, reportedTotalTokens)),
            inputTokens + outputTokens);
        return new TokenTotals(inputTokens, outputTokens, totalTokens);
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

    private static string BuildStartupHandshakeExitMessage(
        ICodexProcessSession session,
        ConcurrentQueue<string> startupDiagnostics)
    {
        var message = "Codex app-server exited before completing the startup handshake.";

        if (session.ExitCode is { } exitCode)
        {
            message += $" exit_code={exitCode}.";
        }

        var diagnostics = string.Join(" | ", startupDiagnostics.ToArray());
        if (!string.IsNullOrWhiteSpace(diagnostics))
        {
            message += $" stderr={diagnostics}";
        }

        return message;
    }

    private static void RecordStartupDiagnostic(ConcurrentQueue<string> startupDiagnostics, string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        startupDiagnostics.Enqueue(line.Trim());
        while (startupDiagnostics.Count > 8 && startupDiagnostics.TryDequeue(out _))
        {
        }
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

    private static bool TryGetResponseId(JsonElement payload, out object requestId)
    {
        requestId = default!;

        if (!payload.TryGetProperty("id", out var idElement))
        {
            return false;
        }

        switch (idElement.ValueKind)
        {
            case JsonValueKind.String:
                requestId = idElement.GetString()!;
                return true;
            case JsonValueKind.Number:
                requestId = idElement.Clone();
                return true;
            default:
                return false;
        }
    }

    private static string CreateOutboundTranscriptTitle(string line)
    {
        using var document = JsonDocument.Parse(line);
        var payload = document.RootElement;
        var method = TryGetMethod(payload);
        if (!string.IsNullOrWhiteSpace(method))
        {
            return $"Sent {method}";
        }

        var id = TryGetId(payload);
        return !string.IsNullOrWhiteSpace(id)
            ? $"Sent response {id}"
            : "Sent payload";
    }

    private static string CreateInboundTranscriptTitle(JsonElement payload)
    {
        var method = TryGetMethod(payload);
        if (!string.IsNullOrWhiteSpace(method))
        {
            return $"Received {method}";
        }

        var id = TryGetId(payload);
        return !string.IsNullOrWhiteSpace(id)
            ? $"Received response {id}"
            : "Received payload";
    }

    private static bool IsAgentMessageDelta(JsonElement payload)
    {
        var method = TryGetMethod(payload);
        return string.Equals(method, "item/agentMessage/delta", StringComparison.Ordinal);
    }

    private static bool IsToolRequestUserInputMethod(string method)
    {
        return string.Equals(method, "item/tool/requestUserInput", StringComparison.Ordinal);
    }

    private static bool IsMcpServerElicitationRequestMethod(string method)
    {
        return string.Equals(method, "mcpServer/elicitation/request", StringComparison.Ordinal);
    }

    private static IssueBlockingException CreateBlockingException(string method, JsonElement payload)
    {
        var options = ExtractFollowUpOptions(payload);
        var errorMessage = ExtractMessage(payload) ?? "Codex app-server requested human intervention.";

        if (IsMcpServerElicitationRequestMethod(method))
        {
            return new IssueBlockingException(
                BlockingReasonCode.ManualDecisionRequired,
                errorMessage,
                "Review the requested manual decision, then resolve the follow-up action to resume the run.",
                options);
        }

        if (IsApprovalRequest(payload))
        {
            return new IssueBlockingException(
                BlockingReasonCode.ApprovalRequired,
                errorMessage,
                "Review the requested approval, then resolve the follow-up action to resume the run.",
                options);
        }

        return new IssueBlockingException(
            BlockingReasonCode.InputRequired,
            errorMessage,
            "Provide the required input or choose an option, then resolve the follow-up action to resume the run.",
            options);
    }

    private static bool TryCreateToolRequestUserInputApprovalResponse(JsonElement payload, out object response)
    {
        response = default!;

        if (!payload.TryGetProperty("params", out var parameters)
            || parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("questions", out var questionsElement)
            || questionsElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var answers = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (var question in questionsElement.EnumerateArray())
        {
            if (question.ValueKind != JsonValueKind.Object
                || !question.TryGetProperty("id", out var questionIdElement)
                || questionIdElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(questionIdElement.GetString())
                || !question.TryGetProperty("options", out var optionsElement)
                || optionsElement.ValueKind != JsonValueKind.Array
                || !TrySelectAutoApprovalOption(optionsElement, out var selectedOptionLabel))
            {
                return false;
            }

            answers[questionIdElement.GetString()!] = new
            {
                answers = new[] { selectedOptionLabel }
            };
        }

        response = new
        {
            answers
        };
        return answers.Count > 0;
    }

    private static bool TryCreateMcpToolCallElicitationApprovalResponse(JsonElement payload, out object response)
    {
        response = default!;

        if (!payload.TryGetProperty("params", out var parameters)
            || parameters.ValueKind != JsonValueKind.Object
            || !TryGetApprovalKind(parameters, out var approvalKind)
            || !string.Equals(approvalKind, "mcp_tool_call", StringComparison.Ordinal)
            || !HasFormMode(parameters)
            || !HasObjectRequestedSchema(parameters))
        {
            return false;
        }

        response = new
        {
            action = "accept",
            content = new Dictionary<string, object?>(StringComparer.Ordinal)
        };
        return true;
    }

    private static bool TryGetApprovalKind(JsonElement parameters, out string approvalKind)
    {
        approvalKind = string.Empty;

        if (!parameters.TryGetProperty("_meta", out var metadata)
            || metadata.ValueKind != JsonValueKind.Object
            || !metadata.TryGetProperty("codex_approval_kind", out var approvalKindElement)
            || approvalKindElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var approvalKindValue = approvalKindElement.GetString();
        if (string.IsNullOrWhiteSpace(approvalKindValue))
        {
            return false;
        }

        approvalKind = approvalKindValue;
        return true;
    }

    private static bool HasFormMode(JsonElement parameters)
    {
        return !parameters.TryGetProperty("mode", out var modeElement)
               || (modeElement.ValueKind == JsonValueKind.String
                   && string.Equals(modeElement.GetString(), "form", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasObjectRequestedSchema(JsonElement parameters)
    {
        return parameters.TryGetProperty("requestedSchema", out var requestedSchema)
               && requestedSchema.ValueKind == JsonValueKind.Object
               && requestedSchema.TryGetProperty("type", out var typeElement)
               && typeElement.ValueKind == JsonValueKind.String
               && string.Equals(typeElement.GetString(), "object", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TrySelectAutoApprovalOption(JsonElement optionsElement, out string selectedOptionLabel)
    {
        selectedOptionLabel = string.Empty;
        string? allowOnceLabel = null;

        foreach (var option in optionsElement.EnumerateArray())
        {
            if (option.ValueKind != JsonValueKind.Object
                || !option.TryGetProperty("label", out var labelElement)
                || labelElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var label = labelElement.GetString();
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            if (string.Equals(label, "Allow for this session", StringComparison.OrdinalIgnoreCase)
                || string.Equals(label, "Approve this Session", StringComparison.OrdinalIgnoreCase))
            {
                selectedOptionLabel = label;
                return true;
            }

            if (allowOnceLabel is null
                && (string.Equals(label, "Allow", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(label, "Approve Once", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(label, "Approve", StringComparison.OrdinalIgnoreCase)))
            {
                allowOnceLabel = label;
            }
        }

        if (allowOnceLabel is null)
        {
            return false;
        }

        selectedOptionLabel = allowOnceLabel;
        return true;
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
            FindFirstInt(payload, static name => name is "cached_tokens" or "cachedTokens"),
            FindFirstInt(payload, static name => name is "output_tokens" or "outputTokens"),
            FindFirstInt(payload, static name => name is "reasoning_tokens" or "reasoningTokens"),
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

    private static bool IsApprovalRequest(JsonElement payload)
    {
        if (payload.TryGetProperty("params", out var parameters)
            && parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty("questions", out var questionsElement)
            && questionsElement.ValueKind == JsonValueKind.Array)
        {
            return questionsElement.EnumerateArray().Any(question =>
                question.ValueKind == JsonValueKind.Object
                && question.TryGetProperty("options", out var optionsElement)
                && optionsElement.ValueKind == JsonValueKind.Array
                && optionsElement.GetArrayLength() > 0);
        }

        return false;
    }

    private static IReadOnlyList<FollowUpActionOptionSnapshot> ExtractFollowUpOptions(JsonElement payload)
    {
        if (!payload.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<FollowUpActionOptionSnapshot>();
        }

        if (parameters.TryGetProperty("questions", out var questionsElement) && questionsElement.ValueKind == JsonValueKind.Array)
        {
            var options = new List<FollowUpActionOptionSnapshot>();
            foreach (var question in questionsElement.EnumerateArray())
            {
                if (question.ValueKind != JsonValueKind.Object
                    || !question.TryGetProperty("options", out var optionsElement)
                    || optionsElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var option in optionsElement.EnumerateArray())
                {
                    if (option.ValueKind != JsonValueKind.Object
                        || !option.TryGetProperty("label", out var labelElement)
                        || labelElement.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var label = labelElement.GetString();
                    if (string.IsNullOrWhiteSpace(label))
                    {
                        continue;
                    }

                    var id = option.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(idElement.GetString())
                        ? idElement.GetString()!
                        : label.Trim().ToLowerInvariant().Replace(' ', '-');
                    var description = option.TryGetProperty("description", out var descriptionElement) && descriptionElement.ValueKind == JsonValueKind.String
                        ? descriptionElement.GetString()
                        : null;
                    options.Add(new FollowUpActionOptionSnapshot(id, label.Trim(), string.IsNullOrWhiteSpace(description) ? null : description.Trim()));
                }
            }

            return options;
        }

        return Array.Empty<FollowUpActionOptionSnapshot>();
    }

    private readonly record struct UsageInfo(
        int? InputTokens,
        int? CachedInputTokens,
        int? OutputTokens,
        int? ReasoningTokens,
        int? TotalTokens)
    {
        public bool HasAny =>
            InputTokens is not null
            || CachedInputTokens is not null
            || OutputTokens is not null
            || ReasoningTokens is not null
            || TotalTokens is not null;
    }

    private readonly record struct TokenComparisonInfo(
        SessionTokenComparisonStatus Status,
        int InputDelta,
        int OutputDelta,
        int TotalDelta);

    private readonly record struct TokenTotals(
        int InputTokens,
        int OutputTokens,
        int TotalTokens);

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

internal readonly record struct CodexProcessStartPlan(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory);

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

        Exception? lastException = null;

        foreach (var startPlan in CodexProcessStartPlanFactory.Create(request))
        {
            try
            {
                var process = new Process
                {
                    StartInfo = CreateStartInfo(startPlan)
                };

                if (!process.Start())
                {
                    process.Dispose();
                    continue;
                }

                return Task.FromResult<ICodexProcessSession>(new ProcessCodexProcessSession(process));
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                lastException = exception;
            }
        }

        var attemptedLaunchers = string.Join(", ", CodexProcessStartPlanFactory.Create(request).Select(plan => plan.FileName));
        var message = $"Failed to launch Codex command '{request.Command}' using: {attemptedLaunchers}.";
        if (lastException is not null)
        {
            message += $" {lastException.Message}";
        }

        throw new CodexAgentException("codex_not_found", message, lastException);
    }

    private static ProcessStartInfo CreateStartInfo(CodexProcessStartPlan startPlan)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = startPlan.FileName,
            WorkingDirectory = startPlan.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in startPlan.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
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

internal static class CodexProcessStartPlanFactory
{
    public static IReadOnlyList<CodexProcessStartPlan> Create(CodexProcessStartRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Command);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkingDirectory);

        return OperatingSystem.IsWindows()
            ? CreateWindowsPlans(request)
            : [new CodexProcessStartPlan("sh", ["-lc", request.Command], request.WorkingDirectory)];
    }

    public static IReadOnlyList<CodexProcessStartPlan> Create(bool isWindows, CodexProcessStartRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Command);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkingDirectory);

        return isWindows
            ? CreateWindowsPlans(request)
            : [new CodexProcessStartPlan("sh", ["-lc", request.Command], request.WorkingDirectory)];
    }

    private static IReadOnlyList<CodexProcessStartPlan> CreateWindowsPlans(CodexProcessStartRequest request)
    {
        return
        [
            new CodexProcessStartPlan(
                "pwsh",
                ["-NoProfile", "-NonInteractive", "-Command", request.Command],
                request.WorkingDirectory),
            new CodexProcessStartPlan(
                "powershell",
                ["-NoProfile", "-NonInteractive", "-Command", request.Command],
                request.WorkingDirectory)
        ];
    }
}

internal sealed class CodexAgentException(string code, string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException)
{
    public string Code { get; } = code;
}
