using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Symphony.Abstractions.Trackers;
using Symphony.Tracker.Linear;

namespace Symphony.Tracker.Linear.Tests;

public sealed class LinearIssueTrackerClientTests
{
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
    public async Task FetchCandidateIssuesAsync_graphql_errors_throw_linear_tracker_exception()
    {
        using var responseHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "errors": [
                        { "message": "Something went wrong" }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            });
        using var httpClient = new HttpClient(responseHandler);
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<LinearTrackerException>(() => client.FetchCandidateIssuesAsync());

        Assert.Equal("linear_graphql_errors", exception.Code);
        Assert.Contains("Something went wrong", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchCandidateIssuesAsync_pages_results_with_project_slug_filter_and_normalizes_blockers()
    {
        using var responseHandler = new RecordingHttpMessageHandler(request =>
        {
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var after = body.RootElement.GetProperty("variables").GetProperty("after");

            if (after.ValueKind is JsonValueKind.Null)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "data": {
                            "issues": {
                              "nodes": [
                                {
                                  "id": "issue-1",
                                  "identifier": "SYMP-1",
                                  "title": "First issue",
                                  "description": "First body",
                                  "priority": 1,
                                  "branchName": "feature/symp-1",
                                  "url": "https://linear.app/acme/issue/SYMP-1",
                                  "createdAt": "2026-03-16T08:00:00Z",
                                  "updatedAt": "2026-03-16T08:05:00Z",
                                  "labels": {
                                    "nodes": [
                                      { "name": "Backend" },
                                      { "name": "backend" },
                                      { "name": "Human Review" }
                                    ]
                                  },
                                  "state": { "name": "Todo" },
                                  "inverseRelations": {
                                    "nodes": [
                                      {
                                        "type": "blocks",
                                        "relatedIssue": {
                                          "id": "issue-99",
                                          "identifier": "SYMP-99",
                                          "state": { "name": "In Progress" }
                                        }
                                      },
                                      {
                                        "type": "relatesTo",
                                        "relatedIssue": {
                                          "id": "issue-77",
                                          "identifier": "SYMP-77",
                                          "state": { "name": "Done" }
                                        }
                                      }
                                    ]
                                  }
                                }
                              ],
                              "pageInfo": {
                                "hasNextPage": true,
                                "endCursor": "cursor-1"
                              }
                            }
                          }
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "data": {
                        "issues": {
                          "nodes": [
                            {
                              "id": "issue-2",
                              "identifier": "SYMP-2",
                              "title": "Second issue",
                              "description": "Second body",
                              "priority": 2,
                              "branchName": "feature/symp-2",
                              "url": "https://linear.app/acme/issue/SYMP-2",
                              "createdAt": "2026-03-16T09:00:00Z",
                              "updatedAt": "2026-03-16T09:05:00Z",
                              "labels": {
                                "nodes": [
                                  { "name": "Ops" }
                                ]
                              },
                              "state": { "name": "In Progress" },
                              "inverseRelations": {
                                "nodes": []
                              }
                            }
                          ],
                          "pageInfo": {
                            "hasNextPage": false,
                            "endCursor": null
                          }
                        }
                      }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });
        using var httpClient = new HttpClient(responseHandler);
        var client = CreateClient(httpClient);

        var issues = await client.FetchCandidateIssuesAsync();

        Assert.Equal(["issue-1", "issue-2"], issues.Select(static issue => issue.Id).ToArray());
        Assert.Equal(["SYMP-1", "SYMP-2"], issues.Select(static issue => issue.Identifier).ToArray());
        Assert.Equal(["backend", "human review"], issues[0].Labels.OrderBy(static label => label, StringComparer.Ordinal).ToArray());
        Assert.Single(issues[0].BlockedBy);
        Assert.Equal("issue-99", issues[0].BlockedBy[0].Id);
        Assert.Equal("SYMP-99", issues[0].BlockedBy[0].Identifier);
        Assert.Equal("In Progress", issues[0].BlockedBy[0].State);

        Assert.Equal(2, responseHandler.Requests.Count);

        var firstRequest = responseHandler.Requests[0];
        Assert.Equal("token", firstRequest.Authorization);
        Assert.Contains("project: { slugId: { eq: $projectSlug } }", firstRequest.Query, StringComparison.Ordinal);
        Assert.Equal("symphony", firstRequest.Variables.GetProperty("projectSlug").GetString());
        Assert.True(firstRequest.Variables.GetProperty("after").ValueKind is JsonValueKind.Null);

        var secondRequest = responseHandler.Requests[1];
        Assert.Equal("cursor-1", secondRequest.Variables.GetProperty("after").GetString());
    }

    [Fact]
    public async Task FetchCandidateIssuesAsync_missing_end_cursor_throws_linear_tracker_exception()
    {
        using var responseHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "data": {
                        "issues": {
                          "nodes": [],
                          "pageInfo": {
                            "hasNextPage": true,
                            "endCursor": null
                          }
                        }
                      }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            });
        using var httpClient = new HttpClient(responseHandler);
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<LinearTrackerException>(() => client.FetchCandidateIssuesAsync());

        Assert.Equal("linear_missing_end_cursor", exception.Code);
    }

    [Fact]
    public async Task FetchIssueStatesByIdsAsync_uses_graphql_id_array_and_preserves_requested_order()
    {
        using var responseHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "data": {
                        "issues": {
                          "nodes": [
                            {
                              "id": "issue-2",
                              "identifier": "SYMP-2",
                              "title": "Second issue",
                              "description": "Second body",
                              "priority": 2,
                              "branchName": null,
                              "url": "https://linear.app/acme/issue/SYMP-2",
                              "createdAt": "2026-03-16T09:00:00Z",
                              "updatedAt": "2026-03-16T09:05:00Z",
                              "labels": { "nodes": [] },
                              "state": { "name": "Done" },
                              "inverseRelations": { "nodes": [] }
                            },
                            {
                              "id": "issue-1",
                              "identifier": "SYMP-1",
                              "title": "First issue",
                              "description": "First body",
                              "priority": 1,
                              "branchName": null,
                              "url": "https://linear.app/acme/issue/SYMP-1",
                              "createdAt": "2026-03-16T08:00:00Z",
                              "updatedAt": "2026-03-16T08:05:00Z",
                              "labels": { "nodes": [] },
                              "state": { "name": "Todo" },
                              "inverseRelations": { "nodes": [] }
                            }
                          ],
                          "pageInfo": {
                            "hasNextPage": false,
                            "endCursor": null
                          }
                        }
                      }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            });
        using var httpClient = new HttpClient(responseHandler);
        var client = CreateClient(httpClient);

        var issues = await client.FetchIssueStatesByIdsAsync(["issue-1", "issue-2"]);

        Assert.Equal(["issue-1", "issue-2"], issues.Select(static issue => issue.Id).ToArray());
        Assert.Single(responseHandler.Requests);
        Assert.Contains("$issueIds: [ID!]!", responseHandler.Requests[0].Query, StringComparison.Ordinal);
        Assert.Equal(
            ["issue-1", "issue-2"],
            responseHandler.Requests[0]
                .Variables
                .GetProperty("issueIds")
                .EnumerateArray()
                .Select(static value => value.GetString() ?? string.Empty)
                .ToArray());
    }

    private static LinearIssueTrackerClient CreateClient(HttpClient httpClient)
    {
        return new LinearIssueTrackerClient(
            httpClient,
            new StaticTrackerClientOptionsProvider(
                new TrackerClientOptions(
                    TrackerAdapterKinds.Linear,
                    "https://api.linear.app/graphql",
                    "token",
                    null,
                    "symphony",
                    null,
                    null,
                    ["Todo", "In Progress"],
                    ["Done"])),
            NullLogger<LinearIssueTrackerClient>.Instance);
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
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(await CapturedRequest.CreateAsync(request, cancellationToken));
            return responseFactory(request);
        }
    }

    private sealed record CapturedRequest(
        string? Authorization,
        string Query,
        JsonElement Variables)
    {
        public static async Task<CapturedRequest> CreateAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.Content is null
                ? "{}"
                : await request.Content.ReadAsStringAsync(cancellationToken);

            using var document = JsonDocument.Parse(content);
            return new CapturedRequest(
                request.Headers.Authorization?.Parameter ?? request.Headers.GetValues("Authorization").SingleOrDefault(),
                document.RootElement.GetProperty("query").GetString() ?? string.Empty,
                document.RootElement.GetProperty("variables").Clone());
        }
    }
}
