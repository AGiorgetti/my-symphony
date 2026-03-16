using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Symphony.Abstractions.Trackers;
using Symphony.Domain.Issues;

namespace Symphony.Tracker.AzureDevOps;

public sealed class AzureDevOpsIssueTrackerClient(
    HttpClient httpClient,
    ITrackerClientOptionsProvider trackerClientOptionsProvider,
    ILogger<AzureDevOpsIssueTrackerClient> logger) : IIssueTrackerClient
{
    private const int DefaultPageSize = 50;
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
            .Select(static id => id?.Trim())
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Select(TryParseId)
            .Where(static id => id is not null)
            .Select(static id => id!.Value)
            .ToArray();

        if (normalizedIds.Length == 0)
        {
            return Array.Empty<Issue>();
        }

        var options = await trackerClientOptionsProvider.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        var project = ParseProject(options);
        var issuesById = await ReadIssuesByIdsAsync(project, normalizedIds, options, cancellationToken).ConfigureAwait(false);

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
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedStates.Length == 0)
        {
            return Array.Empty<Issue>();
        }

        options ??= await trackerClientOptionsProvider.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        var project = ParseProject(options);
        var issueIds = await QueryIssueIdsByStateAsync(project, normalizedStates, options, cancellationToken).ConfigureAwait(false);
        if (issueIds.Length == 0)
        {
            return Array.Empty<Issue>();
        }

        var issuesById = await ReadIssuesByIdsAsync(project, issueIds, options, cancellationToken).ConfigureAwait(false);

        return issueIds
            .Where(issuesById.ContainsKey)
            .Select(id => issuesById[id])
            .ToArray();
    }

    private async Task<int[]> QueryIssueIdsByStateAsync(
        AzureDevOpsProject project,
        IReadOnlyList<string> stateNames,
        TrackerClientOptions options,
        CancellationToken cancellationToken)
    {
        var wiql = BuildWiql(project, stateNames);
        using var request = CreateRequest(
            HttpMethod.Post,
            BuildWiqlUri(options.Endpoint, project),
            options.ApiKey,
            JsonSerializer.Serialize(new { query = wiql }, SerializerOptions));
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await ReadPayloadAsync<AzureDevOpsWiqlResponse>(response, cancellationToken).ConfigureAwait(false);
        if (payload.WorkItems is null)
        {
            throw new AzureDevOpsTrackerException(
                "azure_devops_unknown_payload",
                "Azure DevOps WIQL response was missing work item results.");
        }

        logger.LogDebug(
            "tracker_fetch completed tracker_kind=azure_devops organization={organization} project={project} issue_count={issue_count} outcome=completed",
            project.Organization,
            project.Project,
            payload.WorkItems.Count);

        return payload.WorkItems
            .Select(static item => item.Id)
            .Where(static id => id is not null)
            .Select(static id => id!.Value)
            .ToArray();
    }

    private async Task<Dictionary<int, Issue>> ReadIssuesByIdsAsync(
        AzureDevOpsProject project,
        IReadOnlyList<int> issueIds,
        TrackerClientOptions options,
        CancellationToken cancellationToken)
    {
        var issuesById = new Dictionary<int, Issue>();

        foreach (var batch in Batch(issueIds, DefaultPageSize))
        {
            using var request = CreateRequest(
                HttpMethod.Post,
                BuildWorkItemsBatchUri(options.Endpoint, project),
                options.ApiKey,
                JsonSerializer.Serialize(
                    new
                    {
                        ids = batch,
                        fields = new[]
                        {
                            "System.Title",
                            "System.Description",
                            "System.State",
                            "System.CreatedDate",
                            "System.ChangedDate",
                            "System.Tags",
                            "Microsoft.VSTS.Common.Priority"
                        },
                        expand = "Relations"
                    },
                    SerializerOptions));
            using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await ReadPayloadAsync<AzureDevOpsWorkItemsBatchResponse>(response, cancellationToken).ConfigureAwait(false);
            if (payload.Value is null)
            {
                throw new AzureDevOpsTrackerException(
                    "azure_devops_unknown_payload",
                    "Azure DevOps work item batch response was missing item values.");
            }

            foreach (var workItem in payload.Value)
            {
                var issue = NormalizeIssue(workItem);
                issuesById[int.Parse(issue.Id, CultureInfo.InvariantCulture)] = issue;
            }
        }

        return issuesById;
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

            throw new AzureDevOpsTrackerException(
                "azure_devops_api_status",
                $"Azure DevOps API request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}). Body: {body}");
        }
        catch (AzureDevOpsTrackerException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            throw new AzureDevOpsTrackerException(
                "azure_devops_api_request",
                "Azure DevOps API request failed before a response was received.",
                exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AzureDevOpsTrackerException(
                "azure_devops_api_request",
                "Azure DevOps API request timed out.",
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

            return payload ?? throw new AzureDevOpsTrackerException(
                "azure_devops_unknown_payload",
                "Azure DevOps API returned an empty JSON payload.");
        }
        catch (JsonException exception)
        {
            throw new AzureDevOpsTrackerException(
                "azure_devops_unknown_payload",
                "Azure DevOps API returned malformed JSON.",
                exception);
        }
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        Uri requestUri,
        string apiKey,
        string? body = null)
    {
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($":{apiKey}")));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static Issue NormalizeIssue(AzureDevOpsWorkItemPayload payload)
    {
        if (payload.Id is null || payload.Fields is null)
        {
            throw new AzureDevOpsTrackerException(
                "azure_devops_unknown_payload",
                "Azure DevOps work item payload was missing required fields.");
        }

        var title = GetRequiredStringField(payload.Fields, "System.Title");
        var state = GetRequiredStringField(payload.Fields, "System.State");

        return new Issue(
            id: payload.Id.Value.ToString(CultureInfo.InvariantCulture),
            identifier: $"ADO-{payload.Id.Value}",
            title: title,
            description: GetOptionalStringField(payload.Fields, "System.Description"),
            priority: GetOptionalIntField(payload.Fields, "Microsoft.VSTS.Common.Priority"),
            state: state,
            branchName: null,
            url: payload.Links?.Html?.Href,
            labels: ParseTags(GetOptionalStringField(payload.Fields, "System.Tags")),
            blockedBy: ParseBlockedBy(payload.Relations),
            createdAt: GetOptionalDateField(payload.Fields, "System.CreatedDate"),
            updatedAt: GetOptionalDateField(payload.Fields, "System.ChangedDate"));
    }

    private static IReadOnlyList<IssueBlocker> ParseBlockedBy(IReadOnlyList<AzureDevOpsRelationPayload>? relations)
    {
        if (relations is null)
        {
            return Array.Empty<IssueBlocker>();
        }

        return relations
            .Where(static relation => IsBlockingRelation(relation.Rel))
            .Select(static relation => CreateBlocker(relation.Url))
            .Where(static blocker => blocker is not null)
            .Select(static blocker => blocker!)
            .ToArray();
    }

    private static IssueBlocker? CreateBlocker(string? relationUrl)
    {
        var id = TryParseRelatedWorkItemId(relationUrl);
        return id is null
            ? null
            : new IssueBlocker(
                id.Value.ToString(CultureInfo.InvariantCulture),
                $"ADO-{id.Value}",
                null);
    }

    private static bool IsBlockingRelation(string? relationType)
    {
        if (string.IsNullOrWhiteSpace(relationType))
        {
            return false;
        }

        return relationType.Contains("dependency-reverse", StringComparison.OrdinalIgnoreCase)
            || relationType.Contains("predecessor-reverse", StringComparison.OrdinalIgnoreCase);
    }

    private static int? TryParseRelatedWorkItemId(string? relationUrl)
    {
        if (string.IsNullOrWhiteSpace(relationUrl))
        {
            return null;
        }

        var lastSegment = relationUrl.TrimEnd('/').Split('/').LastOrDefault();
        return int.TryParse(lastSegment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId)
            ? parsedId
            : null;
    }

    private static IReadOnlyList<string> ParseTags(string? rawTags)
    {
        if (string.IsNullOrWhiteSpace(rawTags))
        {
            return Array.Empty<string>();
        }

        return rawTags
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToArray();
    }

    private static string GetRequiredStringField(
        IReadOnlyDictionary<string, JsonElement> fields,
        string fieldName)
    {
        return GetOptionalStringField(fields, fieldName)
            ?? throw new AzureDevOpsTrackerException(
                "azure_devops_unknown_payload",
                $"Azure DevOps work item payload was missing required field '{fieldName}'.");
    }

    private static string? GetOptionalStringField(
        IReadOnlyDictionary<string, JsonElement> fields,
        string fieldName)
    {
        if (!fields.TryGetValue(fieldName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
            _ => throw new AzureDevOpsTrackerException(
                "azure_devops_unknown_payload",
                $"Azure DevOps work item field '{fieldName}' has an unsupported value type.")
        };
    }

    private static int? GetOptionalIntField(
        IReadOnlyDictionary<string, JsonElement> fields,
        string fieldName)
    {
        if (!fields.TryGetValue(fieldName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var intValue))
        {
            return intValue;
        }

        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
        {
            return parsedValue;
        }

        return null;
    }

    private static DateTimeOffset? GetOptionalDateField(
        IReadOnlyDictionary<string, JsonElement> fields,
        string fieldName)
    {
        var text = GetOptionalStringField(fields, fieldName);
        if (text is null)
        {
            return null;
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedValue)
            ? parsedValue
            : throw new AzureDevOpsTrackerException(
                "azure_devops_unknown_payload",
                $"Azure DevOps work item field '{fieldName}' contained an invalid timestamp.");
    }

    private static AzureDevOpsProject ParseProject(TrackerClientOptions options)
    {
        if (!TrackerAdapterKinds.TryNormalize(options.Kind, out var trackerKind) || trackerKind != TrackerAdapterKinds.AzureDevOps)
        {
            throw new InvalidOperationException($"Tracker client options must be for '{TrackerAdapterKinds.AzureDevOps}'.");
        }

        if (string.IsNullOrWhiteSpace(options.Organization) || string.IsNullOrWhiteSpace(options.Project))
        {
            throw new InvalidOperationException("tracker.organization and tracker.project must both be configured.");
        }

        return new AzureDevOpsProject(options.Organization, options.Project);
    }

    private static Uri BuildWiqlUri(string endpoint, AzureDevOpsProject project)
    {
        return BuildUri(endpoint, project, "_apis/wit/wiql?api-version=7.1");
    }

    private static Uri BuildWorkItemsBatchUri(string endpoint, AzureDevOpsProject project)
    {
        return BuildUri(endpoint, project, "_apis/wit/workitemsbatch?api-version=7.1");
    }

    private static Uri BuildUri(string endpoint, AzureDevOpsProject project, string relativePath)
    {
        var normalizedEndpoint = endpoint.TrimEnd('/');
        var escapedOrganization = Uri.EscapeDataString(project.Organization);
        var escapedProject = Uri.EscapeDataString(project.Project);
        return new Uri($"{normalizedEndpoint}/{escapedOrganization}/{escapedProject}/{relativePath}", UriKind.Absolute);
    }

    private static string BuildWiql(AzureDevOpsProject project, IReadOnlyCollection<string> stateNames)
    {
        var escapedStates = string.Join(
            ", ",
            stateNames.Select(static state => $"'{EscapeWiqlLiteral(state)}'"));

        return
            $$"""
            SELECT [System.Id]
            FROM WorkItems
            WHERE [System.TeamProject] = '{{EscapeWiqlLiteral(project.Project)}}'
              AND [System.State] IN ({{escapedStates}})
            ORDER BY [Microsoft.VSTS.Common.Priority] ASC, [System.CreatedDate] ASC
            """;
    }

    private static string EscapeWiqlLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static int? TryParseId(string? rawValue)
    {
        return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue)
            ? parsedValue
            : null;
    }

    private static IEnumerable<int[]> Batch(IReadOnlyList<int> issueIds, int batchSize)
    {
        for (var index = 0; index < issueIds.Count; index += batchSize)
        {
            var remaining = Math.Min(batchSize, issueIds.Count - index);
            var batch = new int[remaining];
            for (var offset = 0; offset < remaining; offset++)
            {
                batch[offset] = issueIds[index + offset];
            }

            yield return batch;
        }
    }

    private sealed record AzureDevOpsProject(string Organization, string Project);

    private sealed record AzureDevOpsWiqlResponse(
        [property: JsonPropertyName("workItems")] IReadOnlyList<AzureDevOpsWorkItemReference>? WorkItems);

    private sealed record AzureDevOpsWorkItemReference([property: JsonPropertyName("id")] int? Id);

    private sealed record AzureDevOpsWorkItemsBatchResponse(
        [property: JsonPropertyName("value")] IReadOnlyList<AzureDevOpsWorkItemPayload>? Value);

    private sealed record AzureDevOpsWorkItemPayload(
        [property: JsonPropertyName("id")] int? Id,
        [property: JsonPropertyName("fields")] IReadOnlyDictionary<string, JsonElement>? Fields,
        [property: JsonPropertyName("relations")] IReadOnlyList<AzureDevOpsRelationPayload>? Relations,
        [property: JsonPropertyName("_links")] AzureDevOpsLinksPayload? Links);

    private sealed record AzureDevOpsRelationPayload(
        [property: JsonPropertyName("rel")] string? Rel,
        [property: JsonPropertyName("url")] string? Url);

    private sealed record AzureDevOpsLinksPayload([property: JsonPropertyName("html")] AzureDevOpsLinkPayload? Html);

    private sealed record AzureDevOpsLinkPayload([property: JsonPropertyName("href")] string? Href);
}
