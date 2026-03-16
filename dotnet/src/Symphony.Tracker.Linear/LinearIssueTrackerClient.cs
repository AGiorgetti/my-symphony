using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Symphony.Abstractions.Trackers;
using Symphony.Domain.Issues;

namespace Symphony.Tracker.Linear;

public sealed class LinearIssueTrackerClient(
    HttpClient httpClient,
    ITrackerClientOptionsProvider trackerClientOptionsProvider,
    ILogger<LinearIssueTrackerClient> logger) : IIssueTrackerClient
{
    private const string CandidateIssuesQuery =
        """
        query CandidateIssues($projectSlug: String!, $stateNames: [String!]!, $after: String) {
          issues(
            first: 50
            after: $after
            filter: {
              project: { slugId: { eq: $projectSlug } }
              state: { name: { in: $stateNames } }
            }
          ) {
            nodes {
              id
              identifier
              title
              description
              priority
              branchName
              url
              createdAt
              updatedAt
              labels {
                nodes {
                  name
                }
              }
              state {
                name
              }
              inverseRelations {
                nodes {
                  type
                  relatedIssue {
                    id
                    identifier
                    state {
                      name
                    }
                  }
                }
              }
            }
            pageInfo {
              hasNextPage
              endCursor
            }
          }
        }
        """;
    private const string IssueStatesQuery =
        """
        query IssueStates($issueIds: [ID!]!, $after: String) {
          issues(
            first: 50
            after: $after
            filter: {
              id: { in: $issueIds }
            }
          ) {
            nodes {
              id
              identifier
              title
              description
              priority
              branchName
              url
              createdAt
              updatedAt
              labels {
                nodes {
                  name
                }
              }
              state {
                name
              }
              inverseRelations {
                nodes {
                  type
                  relatedIssue {
                    id
                    identifier
                    state {
                      name
                    }
                  }
                }
              }
            }
            pageInfo {
              hasNextPage
              endCursor
            }
          }
        }
        """;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public Task<IReadOnlyList<Issue>> FetchCandidateIssuesAsync(CancellationToken cancellationToken = default)
    {
        return FetchIssuesByStatesInternalAsync(options => options.ActiveStates, cancellationToken);
    }

    public Task<IReadOnlyList<Issue>> FetchIssuesByStatesAsync(
        IReadOnlyCollection<string> stateNames,
        CancellationToken cancellationToken = default)
    {
        return FetchIssuesAsync(stateNames, cancellationToken);
    }

    public async Task<IReadOnlyList<Issue>> FetchIssueStatesByIdsAsync(
        IReadOnlyCollection<string> issueIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(issueIds);

        var normalizedIds = issueIds
            .Select(static id => (id ?? string.Empty).Trim())
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalizedIds.Length == 0)
        {
            return Array.Empty<Issue>();
        }

        var options = await trackerClientOptionsProvider.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        var issueConnection = await FetchIssueConnectionAsync(
            IssueStatesQuery,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["issueIds"] = normalizedIds
            },
            options,
            cancellationToken).ConfigureAwait(false);

        var issuesById = issueConnection
            .Select(NormalizeIssue)
            .ToDictionary(issue => issue.Id, StringComparer.Ordinal);

        return normalizedIds
            .Where(issuesById.ContainsKey)
            .Select(id => issuesById[id])
            .ToArray();
    }

    private Task<IReadOnlyList<Issue>> FetchIssuesByStatesInternalAsync(
        Func<TrackerClientOptions, IReadOnlyCollection<string>> stateSelector,
        CancellationToken cancellationToken)
    {
        return FetchIssuesAsync(stateSelector, cancellationToken);
    }

    private async Task<IReadOnlyList<Issue>> FetchIssuesAsync(
        Func<TrackerClientOptions, IReadOnlyCollection<string>> stateSelector,
        CancellationToken cancellationToken)
    {
        var options = await trackerClientOptionsProvider.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        return await FetchIssuesAsync(stateSelector(options), cancellationToken, options).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<Issue>> FetchIssuesAsync(
        IReadOnlyCollection<string> stateNames,
        CancellationToken cancellationToken,
        TrackerClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stateNames);

        var normalizedStates = stateNames
            .Select(static state => (state ?? string.Empty).Trim())
            .Where(static state => !string.IsNullOrWhiteSpace(state))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalizedStates.Length == 0)
        {
            return Array.Empty<Issue>();
        }

        options ??= await trackerClientOptionsProvider.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        var issueConnection = await FetchIssueConnectionAsync(
            CandidateIssuesQuery,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["projectSlug"] = ParseProjectSlug(options),
                ["stateNames"] = normalizedStates
            },
            options,
            cancellationToken).ConfigureAwait(false);

        return issueConnection
            .Select(NormalizeIssue)
            .ToArray();
    }

    private async Task<IReadOnlyList<LinearIssuePayload>> FetchIssueConnectionAsync(
        string query,
        IReadOnlyDictionary<string, object?> variables,
        TrackerClientOptions options,
        CancellationToken cancellationToken)
    {
        var issues = new List<LinearIssuePayload>();
        string? cursor = null;

        while (true)
        {
            var requestVariables = new Dictionary<string, object?>(variables, StringComparer.Ordinal)
            {
                ["after"] = cursor
            };

            var payload = await SendGraphQlRequestAsync<LinearIssuesData>(
                query,
                requestVariables,
                options,
                cancellationToken).ConfigureAwait(false);
            var connection = payload.Issues
                ?? throw new LinearTrackerException(
                    "linear_unknown_payload",
                    "Linear GraphQL response was missing issues connection data.");

            if (connection.Nodes is null || connection.PageInfo is null || connection.PageInfo.HasNextPage is null)
            {
                throw new LinearTrackerException(
                    "linear_unknown_payload",
                    "Linear GraphQL response was missing required pagination fields.");
            }

            logger.LogDebug(
                "tracker_fetch completed tracker_kind=linear project_slug={project_slug} issue_count={issue_count} outcome=completed",
                options.ProjectSlug,
                connection.Nodes.Count);

            issues.AddRange(connection.Nodes);

            if (!connection.PageInfo.HasNextPage.Value)
            {
                return issues;
            }

            cursor = connection.PageInfo.EndCursor;
            if (string.IsNullOrWhiteSpace(cursor))
            {
                throw new LinearTrackerException(
                    "linear_missing_end_cursor",
                    "Linear GraphQL pagination reported hasNextPage=true without an endCursor.");
            }
        }
    }

    private async Task<T> SendGraphQlRequestAsync<T>(
        string query,
        IReadOnlyDictionary<string, object?> variables,
        TrackerClientOptions options,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(options.Endpoint));
        request.Headers.TryAddWithoutValidation("Authorization", options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(
            JsonSerializer.Serialize(new GraphQlRequestPayload(query, variables), SerializerOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await ReadPayloadAsync<GraphQlResponse<T>>(response, cancellationToken).ConfigureAwait(false);

        if (payload.Errors is { Count: > 0 })
        {
            throw new LinearTrackerException(
                "linear_graphql_errors",
                $"Linear GraphQL request returned errors: {string.Join("; ", payload.Errors.Select(static error => error.Message ?? "unknown error"))}");
        }

        return payload.Data
            ?? throw new LinearTrackerException(
                "linear_unknown_payload",
                "Linear GraphQL response was missing the data payload.");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            response.Dispose();

            throw new LinearTrackerException(
                "linear_api_status",
                $"Linear API request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}). Body: {body}");
        }
        catch (LinearTrackerException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            throw new LinearTrackerException(
                "linear_api_request",
                "Linear API request failed before a response was received.",
                exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LinearTrackerException(
                "linear_api_request",
                "Linear API request timed out.",
                exception);
        }
    }

    private static async Task<T> ReadPayloadAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);

            return payload ?? throw new LinearTrackerException(
                "linear_unknown_payload",
                "Linear API returned an empty JSON payload.");
        }
        catch (JsonException exception)
        {
            throw new LinearTrackerException(
                "linear_unknown_payload",
                "Linear API returned malformed JSON.",
                exception);
        }
    }

    private static Issue NormalizeIssue(LinearIssuePayload payload)
    {
        if (payload.Id is null || payload.Identifier is null || payload.Title is null || payload.State?.Name is null)
        {
            throw new LinearTrackerException(
                "linear_unknown_payload",
                "Linear issue payload was missing required fields.");
        }

        return new Issue(
            id: payload.Id,
            identifier: payload.Identifier,
            title: payload.Title,
            description: payload.Description,
            priority: payload.Priority,
            state: payload.State.Name,
            branchName: payload.BranchName,
            url: payload.Url,
            labels: payload.Labels?.Nodes?.Select(static label => label.Name ?? string.Empty).ToArray(),
            blockedBy: ParseBlockedBy(payload.InverseRelations),
            createdAt: payload.CreatedAt,
            updatedAt: payload.UpdatedAt);
    }

    private static IReadOnlyList<IssueBlocker> ParseBlockedBy(LinearRelationConnection? inverseRelations)
    {
        if (inverseRelations?.Nodes is null)
        {
            return Array.Empty<IssueBlocker>();
        }

        return inverseRelations.Nodes
            .Where(static relation => string.Equals(relation.Type, "blocks", StringComparison.OrdinalIgnoreCase))
            .Where(static relation => relation.RelatedIssue is not null)
            .Select(static relation => relation.RelatedIssue!)
            .Where(static relatedIssue => !string.IsNullOrWhiteSpace(relatedIssue.Id) || !string.IsNullOrWhiteSpace(relatedIssue.Identifier))
            .Select(static relatedIssue => new IssueBlocker(relatedIssue.Id, relatedIssue.Identifier, relatedIssue.State?.Name))
            .ToArray();
    }

    private static string ParseProjectSlug(TrackerClientOptions options)
    {
        if (!TrackerAdapterKinds.TryNormalize(options.Kind, out var trackerKind) || trackerKind != TrackerAdapterKinds.Linear)
        {
            throw new InvalidOperationException($"Tracker client options must be for '{TrackerAdapterKinds.Linear}'.");
        }

        return string.IsNullOrWhiteSpace(options.ProjectSlug)
            ? throw new InvalidOperationException("tracker.project_slug must be configured.")
            : options.ProjectSlug;
    }

    private static Uri BuildEndpoint(string endpoint)
    {
        return new Uri(endpoint, UriKind.Absolute);
    }

    private sealed record GraphQlRequestPayload(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("variables")] IReadOnlyDictionary<string, object?> Variables);

    private sealed record GraphQlResponse<T>(
        [property: JsonPropertyName("data")] T? Data,
        [property: JsonPropertyName("errors")] IReadOnlyList<GraphQlErrorPayload>? Errors);

    private sealed record GraphQlErrorPayload([property: JsonPropertyName("message")] string? Message);

    private sealed record LinearIssuesData([property: JsonPropertyName("issues")] LinearIssueConnection? Issues);

    private sealed record LinearIssueConnection(
        [property: JsonPropertyName("nodes")] IReadOnlyList<LinearIssuePayload>? Nodes,
        [property: JsonPropertyName("pageInfo")] LinearPageInfoPayload? PageInfo);

    private sealed record LinearPageInfoPayload(
        [property: JsonPropertyName("hasNextPage")] bool? HasNextPage,
        [property: JsonPropertyName("endCursor")] string? EndCursor);

    private sealed record LinearIssuePayload(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("identifier")] string? Identifier,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("priority")] int? Priority,
        [property: JsonPropertyName("branchName")] string? BranchName,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("createdAt")] DateTimeOffset? CreatedAt,
        [property: JsonPropertyName("updatedAt")] DateTimeOffset? UpdatedAt,
        [property: JsonPropertyName("labels")] LinearLabelConnection? Labels,
        [property: JsonPropertyName("state")] LinearStatePayload? State,
        [property: JsonPropertyName("inverseRelations")] LinearRelationConnection? InverseRelations);

    private sealed record LinearStatePayload([property: JsonPropertyName("name")] string? Name);

    private sealed record LinearLabelConnection(
        [property: JsonPropertyName("nodes")] IReadOnlyList<LinearLabelPayload>? Nodes);

    private sealed record LinearLabelPayload([property: JsonPropertyName("name")] string? Name);

    private sealed record LinearRelationConnection(
        [property: JsonPropertyName("nodes")] IReadOnlyList<LinearRelationPayload>? Nodes);

    private sealed record LinearRelationPayload(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("relatedIssue")] LinearRelatedIssuePayload? RelatedIssue);

    private sealed record LinearRelatedIssuePayload(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("identifier")] string? Identifier,
        [property: JsonPropertyName("state")] LinearStatePayload? State);
}
