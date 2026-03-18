using System.Text;
using Microsoft.AspNetCore.Mvc;
using Symphony.Abstractions.Orchestration;
using Symphony.Application.Configuration;
using Symphony.Application.Runtime;
using Symphony.Host.Health;

namespace Symphony.Host.Api;

public static class SymphonyApiEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapSymphonyApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/v1");

        group.MapGet(
            "/state",
            async Task<IResult> (
                [FromServices] IOrchestratorRuntimeService runtimeService,
                [FromServices] ServiceHealthSnapshotProvider serviceHealthSnapshotProvider,
                CancellationToken cancellationToken) =>
            {
                var snapshot = await runtimeService.GetStateSnapshotAsync(cancellationToken).ConfigureAwait(false);
                return Results.Ok(ToStateResponse(snapshot, serviceHealthSnapshotProvider.GetSnapshot()));
            });

        group.MapGet(
            "/{issueIdentifier}",
            async Task<IResult> (
                string issueIdentifier,
                [FromServices] IOrchestratorRuntimeService runtimeService,
                [FromServices] IWorkflowOptionsProvider workflowOptionsProvider,
                CancellationToken cancellationToken) =>
            {
                var snapshot = await runtimeService.GetIssueSnapshotAsync(issueIdentifier, cancellationToken).ConfigureAwait(false);
                if (snapshot is null)
                {
                    return Results.NotFound(
                        new ErrorEnvelopeDto(
                            new ErrorDetailsDto(
                                "issue_not_found",
                                $"Issue '{issueIdentifier}' is not tracked by the current in-memory runtime state.")));
                }

                var workspacePath = await ResolveWorkspacePathAsync(
                        snapshot.IssueIdentifier,
                        workflowOptionsProvider,
                        cancellationToken)
                    .ConfigureAwait(false);
                return Results.Ok(ToIssueResponse(snapshot, workspacePath));
            });

        group.MapPost(
            "/refresh",
            IResult ([FromServices] IOrchestratorRuntimeService runtimeService) =>
            {
                var receipt = runtimeService.RequestRefresh();
                return Results.Accepted(
                    uri: null,
                    value: new RefreshResponseDto(
                        receipt.Queued,
                        receipt.Coalesced,
                        receipt.RequestedAt,
                        receipt.Operations));
            });

        return endpoints;
    }

    private static StateResponseDto ToStateResponse(OrchestratorStateSnapshot snapshot, ServiceHealthSnapshot healthSnapshot)
    {
        var running = snapshot.Running
            .Select(
                session => new RunningIssueDto(
                    session.IssueId,
                    session.IssueIdentifier,
                    session.State,
                    session.SessionId,
                    session.TurnCount,
                    session.LastEvent,
                    session.LastMessage,
                    session.StartedAt,
                    session.LastEventAt,
                    new TokenTotalsDto(
                        session.InputTokens,
                        session.OutputTokens,
                        session.TotalTokens)))
            .ToArray();
        var retrying = snapshot.Retrying
            .Select(
                issue => new RetryingIssueDto(
                    issue.IssueId,
                    issue.IssueIdentifier,
                    issue.Attempt,
                    issue.DueAt,
                    issue.Error))
            .ToArray();

        return new StateResponseDto(
            snapshot.GeneratedAt,
            new StateCountsDto(running.Length, retrying.Length),
            new HealthStatusDto(
                healthSnapshot.Status,
                healthSnapshot.LastPollTickAt,
                healthSnapshot.LastSuccessfulPollAt,
                healthSnapshot.LastSuccessfulPollAgeSeconds,
                healthSnapshot.PollIsStale,
                healthSnapshot.WorkflowLoadStatus,
                healthSnapshot.WorkflowLastLoadedAt,
                healthSnapshot.WorkflowPath,
                healthSnapshot.PollLastError,
                healthSnapshot.WorkflowLastError),
            running,
            retrying,
            new CodexTotalsDto(
                snapshot.CodexTotals.InputTokens,
                snapshot.CodexTotals.OutputTokens,
                snapshot.CodexTotals.TotalTokens,
                snapshot.CodexTotals.SecondsRunning),
            snapshot.RateLimits);
    }

    private static IssueResponseDto ToIssueResponse(OrchestratorIssueSnapshot snapshot, string workspacePath)
    {
        return new IssueResponseDto(
            snapshot.IssueIdentifier,
            snapshot.IssueId,
            snapshot.Status,
            new WorkspaceDto(workspacePath),
            new IssueAttemptsDto(snapshot.RestartCount, snapshot.CurrentRetryAttempt),
            snapshot.Running is null
                ? null
                : new RunningIssueDto(
                    snapshot.Running.IssueId,
                    snapshot.Running.IssueIdentifier,
                    snapshot.Running.State,
                    snapshot.Running.SessionId,
                    snapshot.Running.TurnCount,
                    snapshot.Running.LastEvent,
                    snapshot.Running.LastMessage,
                    snapshot.Running.StartedAt,
                    snapshot.Running.LastEventAt,
                    new TokenTotalsDto(
                        snapshot.Running.InputTokens,
                        snapshot.Running.OutputTokens,
                        snapshot.Running.TotalTokens)),
            snapshot.Retry is null
                ? null
                : new RetryingIssueDto(
                    snapshot.Retry.IssueId,
                    snapshot.Retry.IssueIdentifier,
                    snapshot.Retry.Attempt,
                    snapshot.Retry.DueAt,
                    snapshot.Retry.Error),
            new IssueLogsDto(Array.Empty<SessionLogDto>()),
            snapshot.RecentEvents
                .Select(runtimeEvent => new RuntimeEventDto(runtimeEvent.At, runtimeEvent.Event, runtimeEvent.Message))
                .ToArray(),
            snapshot.LastError,
            new Dictionary<string, object?>());
    }

    private static async Task<string> ResolveWorkspacePathAsync(
        string issueIdentifier,
        IWorkflowOptionsProvider workflowOptionsProvider,
        CancellationToken cancellationToken)
    {
        var workflowOptions = await workflowOptionsProvider.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        var workspaceKey = SanitizeIssueIdentifier(issueIdentifier);
        var workspaceRoot = Path.GetFullPath(workflowOptions.Workspace.Root);

        return Path.GetFullPath(Path.Combine(workspaceRoot, workspaceKey));
    }

    private static string SanitizeIssueIdentifier(string issueIdentifier)
    {
        var sanitized = new StringBuilder(issueIdentifier.Trim().Length);
        var previousWasHyphen = false;

        foreach (var character in issueIdentifier.Trim())
        {
            var normalizedCharacter = char.IsLetterOrDigit(character) || character is '.' or '_' or '-'
                ? character
                : '-';
            if (normalizedCharacter == '-' && previousWasHyphen)
            {
                continue;
            }

            sanitized.Append(normalizedCharacter);
            previousWasHyphen = normalizedCharacter == '-';
        }

        var workspaceKey = sanitized.ToString().Trim('-');
        if (string.IsNullOrWhiteSpace(workspaceKey))
        {
            throw new InvalidOperationException("Issue identifier did not produce a valid workspace key.");
        }

        return workspaceKey;
    }
}
