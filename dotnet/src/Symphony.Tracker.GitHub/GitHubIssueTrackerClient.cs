using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Symphony.Abstractions.Trackers;
using Symphony.Domain.Issues;

namespace Symphony.Tracker.GitHub;

public sealed class GitHubIssueTrackerClient(
    HttpClient httpClient,
    ITrackerClientOptionsProvider trackerClientOptionsProvider,
    ILogger<GitHubIssueTrackerClient> logger) : IIssueTrackerClient
{
    private const int DefaultPageSize = 50;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public Task<IReadOnlyList<Issue>> FetchCandidateIssuesAsync(CancellationToken cancellationToken = default)
    {
        return FetchIssuesByStatesInternalAsync(
            options => options.ActiveStates,
            cancellationToken);
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
            .Select(static id => id?.Trim())
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalizedIds.Length == 0)
        {
            return Array.Empty<Issue>();
        }

        var targetIdSet = normalizedIds.ToHashSet(StringComparer.Ordinal);
        var issuesById = new Dictionary<string, Issue>(StringComparer.Ordinal);
        var options = await trackerClientOptionsProvider.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        var repository = ParseRepository(options);

        await foreach (var payload in EnumerateIssuesAsync(repository, stateFilter: "all", options, cancellationToken).ConfigureAwait(false))
        {
            if (payload.PullRequest is not null)
            {
                continue;
            }

            var issue = NormalizeIssue(payload);
            if (targetIdSet.Contains(issue.Id))
            {
                issuesById[issue.Id] = issue;
            }

            if (issuesById.Count == targetIdSet.Count)
            {
                break;
            }
        }

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
            .Select(static state => state?.Trim())
            .Where(static state => !string.IsNullOrWhiteSpace(state))
            .Select(static state => state!.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalizedStates.Length == 0)
        {
            return Array.Empty<Issue>();
        }

        options ??= await trackerClientOptionsProvider.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        var repository = ParseRepository(options);
        var stateFilter = GetGitHubStateFilter(normalizedStates);
        var issues = new List<Issue>();

        await foreach (var payload in EnumerateIssuesAsync(repository, stateFilter, options, cancellationToken).ConfigureAwait(false))
        {
            if (payload.PullRequest is not null)
            {
                continue;
            }

            var issue = NormalizeIssue(payload);
            if (normalizedStates.Contains(issue.NormalizedState, StringComparer.Ordinal))
            {
                issues.Add(issue);
            }
        }

        return issues;
    }

    private async IAsyncEnumerable<GitHubIssuePayload> EnumerateIssuesAsync(
        GitHubRepository repository,
        string stateFilter,
        TrackerClientOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var page = 1; ; page++)
        {
            var requestUri = BuildIssuesRequestUri(options.Endpoint, repository, stateFilter, page);
            using var request = CreateRequest(options.ApiKey, requestUri);
            using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await ReadPayloadAsync(response, cancellationToken).ConfigureAwait(false);

            logger.LogDebug(
                "Fetched GitHub issues page {Page} for {Owner}/{Repository} with state filter {StateFilter}. Count: {Count}",
                page,
                repository.Owner,
                repository.Name,
                stateFilter,
                payload.Count);

            if (payload.Count == 0)
            {
                yield break;
            }

            foreach (var issuePayload in payload)
            {
                yield return issuePayload;
            }

            if (payload.Count < DefaultPageSize)
            {
                yield break;
            }
        }
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

            throw new GitHubTrackerException(
                "github_api_status",
                $"GitHub API request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}). Body: {body}");
        }
        catch (GitHubTrackerException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            throw new GitHubTrackerException(
                "github_api_request",
                "GitHub API request failed before a response was received.",
                exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GitHubTrackerException(
                "github_api_request",
                "GitHub API request timed out.",
                exception);
        }
    }

    private static async Task<List<GitHubIssuePayload>> ReadPayloadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync<List<GitHubIssuePayload>>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            return payload ?? throw new GitHubTrackerException("github_unknown_payload", "GitHub API returned an empty JSON payload.");
        }
        catch (JsonException exception)
        {
            throw new GitHubTrackerException("github_unknown_payload", "GitHub API returned malformed JSON.", exception);
        }
    }

    private static HttpRequestMessage CreateRequest(string apiKey, Uri requestUri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("Symphony");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

        return request;
    }

    private static Issue NormalizeIssue(GitHubIssuePayload payload)
    {
        if (payload.Id is null || payload.Number is null || payload.Title is null || payload.State is null)
        {
            throw new GitHubTrackerException("github_unknown_payload", "GitHub issue payload was missing required fields.");
        }

        return new Issue(
            id: payload.Id.Value.ToString(),
            identifier: $"#{payload.Number.Value}",
            title: payload.Title,
            description: payload.Body,
            priority: null,
            state: payload.State,
            branchName: null,
            url: payload.HtmlUrl,
            labels: payload.Labels?.Select(static label => label.Name ?? string.Empty).ToArray(),
            blockedBy: Array.Empty<IssueBlocker>(),
            createdAt: payload.CreatedAt,
            updatedAt: payload.UpdatedAt);
    }

    private static Uri BuildIssuesRequestUri(string endpoint, GitHubRepository repository, string stateFilter, int page)
    {
        var builder = new UriBuilder($"{endpoint.TrimEnd('/')}/repos/{repository.Owner}/{repository.Name}/issues")
        {
            Query = $"state={Uri.EscapeDataString(stateFilter)}&per_page={DefaultPageSize}&page={page}"
        };

        return builder.Uri;
    }

    private static string GetGitHubStateFilter(IReadOnlyList<string> normalizedStates)
    {
        return normalizedStates.Count == 1 && normalizedStates[0] is "open" or "closed"
            ? normalizedStates[0]
            : "all";
    }

    private static GitHubRepository ParseRepository(TrackerClientOptions options)
    {
        if (!TrackerAdapterKinds.TryNormalize(options.Kind, out var trackerKind) || trackerKind != TrackerAdapterKinds.GitHub)
        {
            throw new InvalidOperationException($"Tracker client options must be for '{TrackerAdapterKinds.GitHub}'.");
        }

        var repository = options.Repository?.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (repository is not [var owner, var name])
        {
            throw new InvalidOperationException("tracker.repository must use the '<owner>/<repo>' format.");
        }

        return new GitHubRepository(owner, name);
    }

    private sealed record GitHubRepository(string Owner, string Name);

    private sealed record GitHubIssuePayload(
        [property: JsonPropertyName("id")] long? Id,
        [property: JsonPropertyName("number")] int? Number,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt,
        [property: JsonPropertyName("updated_at")] DateTimeOffset? UpdatedAt,
        [property: JsonPropertyName("labels")] IReadOnlyList<GitHubLabelPayload>? Labels,
        [property: JsonPropertyName("pull_request")] JsonElement? PullRequest);

    private sealed record GitHubLabelPayload([property: JsonPropertyName("name")] string? Name);
}
