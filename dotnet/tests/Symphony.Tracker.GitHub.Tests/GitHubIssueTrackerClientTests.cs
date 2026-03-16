using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using Symphony.Abstractions.Trackers;
using Symphony.Domain.Issues;
using Symphony.Tracker.GitHub;

namespace Symphony.Tracker.GitHub.Tests;

public sealed class GitHubIssueTrackerClientTests
{
    [Fact]
    public async Task FetchCandidateIssuesAsync_returns_empty_results()
    {
        using var responseHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        using var httpClient = new HttpClient(responseHandler);
        var client = CreateClient(httpClient);

        var issues = await client.FetchCandidateIssuesAsync();

        Assert.Empty(issues);
        Assert.Single(responseHandler.Requests);
        Assert.Equal("open", GetQueryParameter(responseHandler.Requests[0].RequestUri, "state"));
    }

    [Fact]
    public async Task FetchIssuesByStatesAsync_empty_states_returns_empty_without_api_call()
    {
        using var responseHandler = new RecordingHttpMessageHandler(_ =>
            throw new InvalidOperationException("No request expected."));
        using var httpClient = new HttpClient(responseHandler);
        var client = CreateClient(httpClient);

        var issues = await client.FetchIssuesByStatesAsync(Array.Empty<string>());

        Assert.Empty(issues);
        Assert.Empty(responseHandler.Requests);
    }

    [Fact]
    public async Task FetchCandidateIssuesAsync_auth_failure_throws_github_tracker_exception()
    {
        using var responseHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"message\":\"Bad credentials\"}")
            });
        using var httpClient = new HttpClient(responseHandler);
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<GitHubTrackerException>(() => client.FetchCandidateIssuesAsync());

        Assert.Equal("github_api_status", exception.Code);
        Assert.Contains("401", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchCandidateIssuesAsync_pages_results_and_excludes_pull_requests()
    {
        using var responseHandler = new RecordingHttpMessageHandler(request =>
        {
            var page = int.Parse(GetQueryParameter(request.RequestUri, "page")!);
            var payload = page switch
            {
                1 => CreateIssuePagePayload(issueCount: 49, includePullRequest: true, startNumber: 1),
                2 => CreateIssuePagePayload(issueCount: 1, includePullRequest: false, startNumber: 100),
                _ => "[]"
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload)
            };
        });
        using var httpClient = new HttpClient(responseHandler);
        var client = CreateClient(httpClient);

        var issues = await client.FetchCandidateIssuesAsync();

        Assert.Equal(50, issues.Count);
        Assert.Equal("#1", issues[0].Identifier);
        Assert.Equal("#100", issues[^1].Identifier);
        Assert.DoesNotContain(issues, issue => issue.Title.Contains("Pull Request", StringComparison.Ordinal));
        Assert.Equal(2, responseHandler.Requests.Count);
        Assert.All(responseHandler.Requests, request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("token", request.Headers.Authorization?.Parameter);
            Assert.Contains(request.Headers.UserAgent, static value => value.Product?.Name == "Symphony" || value.Comment is not null);
        });
    }

    private static string? GetQueryParameter(Uri? uri, string key)
    {
        if (uri is null || string.IsNullOrEmpty(uri.Query))
        {
            return null;
        }

        var query = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var segment in query)
        {
            var parts = segment.Split('=', 2);
            if (parts.Length == 2 && string.Equals(parts[0], key, StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }

    private static GitHubIssueTrackerClient CreateClient(HttpClient httpClient)
    {
        return new GitHubIssueTrackerClient(
            httpClient,
            new StaticTrackerClientOptionsProvider(
                new TrackerClientOptions(
                    TrackerAdapterKinds.GitHub,
                    "https://api.github.com",
                    "token",
                    "AGiorgetti/my-symphony",
                    null,
                    null,
                    null,
                    ["open"],
                    ["closed"])),
            NullLogger<GitHubIssueTrackerClient>.Instance);
    }

    private static string CreateIssuePagePayload(int issueCount, bool includePullRequest, int startNumber)
    {
        var payload = new List<string>(issueCount + (includePullRequest ? 1 : 0));

        for (var index = 0; index < issueCount; index++)
        {
            var number = startNumber + index;
            payload.Add(
                $$"""
                {
                  "id": {{10_000 + number}},
                  "number": {{number}},
                  "title": "Issue {{number}}",
                  "body": "Body {{number}}",
                  "state": "open",
                  "html_url": "https://github.com/AGiorgetti/my-symphony/issues/{{number}}",
                  "created_at": "2026-03-16T08:00:00Z",
                  "updated_at": "2026-03-16T08:05:00Z",
                  "labels": [
                    { "name": "Priority:P0" }
                  ]
                }
                """);
        }

        if (includePullRequest)
        {
            payload.Add(
                """
                {
                  "id": 99999,
                  "number": 999,
                  "title": "Pull Request 999",
                  "body": "PR body",
                  "state": "open",
                  "html_url": "https://github.com/AGiorgetti/my-symphony/pull/999",
                  "created_at": "2026-03-16T08:00:00Z",
                  "updated_at": "2026-03-16T08:05:00Z",
                  "labels": [],
                  "pull_request": { "url": "https://api.github.com/repos/AGiorgetti/my-symphony/pulls/999" }
                }
                """);
        }

        return "[" + string.Join(",", payload) + "]";
    }

    private sealed class StaticTrackerClientOptionsProvider(TrackerClientOptions options) : ITrackerClientOptionsProvider
    {
        public Task<TrackerClientOptions> GetCurrentAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(options);
        }
    }

    private sealed class RecordingHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));
            return Task.FromResult(responseFactory(request));
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            if (request.Headers.Authorization is AuthenticationHeaderValue authorization)
            {
                clone.Headers.Authorization = new AuthenticationHeaderValue(authorization.Scheme, authorization.Parameter);
            }

            foreach (var header in request.Headers.Where(static header => header.Key != "Authorization"))
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }
    }
}
