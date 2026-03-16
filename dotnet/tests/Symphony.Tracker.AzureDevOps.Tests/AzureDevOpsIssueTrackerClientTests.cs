using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Symphony.Abstractions.Trackers;
using Symphony.Tracker.AzureDevOps;

namespace Symphony.Tracker.AzureDevOps.Tests;

public sealed class AzureDevOpsIssueTrackerClientTests
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
    public async Task FetchCandidateIssuesAsync_auth_failure_throws_azure_devops_tracker_exception()
    {
        using var responseHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"message\":\"Unauthorized\"}", Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(responseHandler);
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<AzureDevOpsTrackerException>(() => client.FetchCandidateIssuesAsync());

        Assert.Equal("azure_devops_api_status", exception.Code);
        Assert.Contains("401", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchCandidateIssuesAsync_queries_wiql_then_batches_and_normalizes_issues()
    {
        using var responseHandler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsoluteUri.EndsWith("/_apis/wit/wiql?api-version=7.1", StringComparison.Ordinal) == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "workItems": [
                            { "id": 42 },
                            { "id": 41 }
                          ]
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
                      "value": [
                        {
                          "id": 42,
                          "fields": {
                            "System.Title": "First Azure issue",
                            "System.Description": "<p>Body</p>",
                            "System.State": "Active",
                            "System.CreatedDate": "2026-03-16T08:00:00Z",
                            "System.ChangedDate": "2026-03-16T08:05:00Z",
                            "System.Tags": "Backend;High Priority;backend",
                            "Microsoft.VSTS.Common.Priority": "1"
                          },
                          "relations": [
                            {
                              "rel": "System.LinkTypes.Dependency-Reverse",
                              "url": "https://dev.azure.com/AGiorgetti/_apis/wit/workItems/99"
                            }
                          ],
                          "_links": {
                            "html": {
                              "href": "https://dev.azure.com/AGiorgetti/my-symphony/_workitems/edit/42"
                            }
                          }
                        },
                        {
                          "id": 41,
                          "fields": {
                            "System.Title": "Second Azure issue",
                            "System.State": "Committed",
                            "System.CreatedDate": "2026-03-16T09:00:00Z",
                            "System.ChangedDate": "2026-03-16T09:05:00Z",
                            "System.Tags": "UX",
                            "Microsoft.VSTS.Common.Priority": "high"
                          },
                          "relations": [
                            {
                              "rel": "System.LinkTypes.Related",
                              "url": "https://dev.azure.com/AGiorgetti/_apis/wit/workItems/55"
                            }
                          ],
                          "_links": {
                            "html": {
                              "href": "https://dev.azure.com/AGiorgetti/my-symphony/_workitems/edit/41"
                            }
                          }
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });
        using var httpClient = new HttpClient(responseHandler);
        var client = CreateClient(httpClient);

        var issues = await client.FetchCandidateIssuesAsync();

        Assert.Equal(["42", "41"], issues.Select(static issue => issue.Id).ToArray());
        Assert.Equal(["ADO-42", "ADO-41"], issues.Select(static issue => issue.Identifier).ToArray());
        Assert.Equal(["backend", "high priority"], issues[0].Labels.OrderBy(static label => label, StringComparer.Ordinal).ToArray());
        Assert.Single(issues[0].BlockedBy);
        Assert.Equal("99", issues[0].BlockedBy[0].Id);
        Assert.Equal("ADO-99", issues[0].BlockedBy[0].Identifier);
        Assert.Equal(1, issues[0].Priority);
        Assert.Null(issues[1].Priority);
        Assert.Equal("https://dev.azure.com/AGiorgetti/my-symphony/_workitems/edit/42", issues[0].Url);
        Assert.Equal(DateTimeOffset.Parse("2026-03-16T08:00:00Z"), issues[0].CreatedAt);
        Assert.Equal(DateTimeOffset.Parse("2026-03-16T08:05:00Z"), issues[0].UpdatedAt);

        Assert.Equal(2, responseHandler.Requests.Count);

        var wiqlRequest = responseHandler.Requests[0];
        Assert.Equal(HttpMethod.Post, wiqlRequest.Method);
        Assert.Equal("Basic", wiqlRequest.AuthorizationScheme);
        Assert.Equal(":token", DecodeBasicCredential(wiqlRequest.AuthorizationParameter));
        using var wiqlBody = JsonDocument.Parse(wiqlRequest.Content);
        var wiqlQuery = wiqlBody.RootElement.GetProperty("query").GetString();
        Assert.Contains("[System.TeamProject] = 'my-symphony'", wiqlQuery, StringComparison.Ordinal);
        Assert.Contains("[System.State] IN ('Active', 'Committed')", wiqlQuery, StringComparison.Ordinal);

        var batchRequest = responseHandler.Requests[1];
        Assert.Equal(HttpMethod.Post, batchRequest.Method);
        Assert.Contains("\"ids\":[42,41]", batchRequest.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchIssueStatesByIdsAsync_preserves_requested_order_and_ignores_non_numeric_ids()
    {
        using var responseHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "value": [
                        {
                          "id": 5,
                          "fields": {
                            "System.Title": "Five",
                            "System.State": "Closed",
                            "System.CreatedDate": "2026-03-16T10:00:00Z",
                            "System.ChangedDate": "2026-03-16T10:05:00Z"
                          },
                          "relations": [],
                          "_links": {
                            "html": {
                              "href": "https://dev.azure.com/AGiorgetti/my-symphony/_workitems/edit/5"
                            }
                          }
                        },
                        {
                          "id": 7,
                          "fields": {
                            "System.Title": "Seven",
                            "System.State": "Active",
                            "System.CreatedDate": "2026-03-16T11:00:00Z",
                            "System.ChangedDate": "2026-03-16T11:05:00Z"
                          },
                          "relations": [],
                          "_links": {
                            "html": {
                              "href": "https://dev.azure.com/AGiorgetti/my-symphony/_workitems/edit/7"
                            }
                          }
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            });
        using var httpClient = new HttpClient(responseHandler);
        var client = CreateClient(httpClient);

        var issues = await client.FetchIssueStatesByIdsAsync(["7", "not-a-number", "5"]);

        Assert.Equal(["7", "5"], issues.Select(static issue => issue.Id).ToArray());
        Assert.Single(responseHandler.Requests);
        Assert.Contains("\"ids\":[7,5]", responseHandler.Requests[0].Content, StringComparison.Ordinal);
    }

    private static AzureDevOpsIssueTrackerClient CreateClient(HttpClient httpClient)
    {
        return new AzureDevOpsIssueTrackerClient(
            httpClient,
            new StaticTrackerClientOptionsProvider(
                new TrackerClientOptions(
                    TrackerAdapterKinds.AzureDevOps,
                    "https://dev.azure.com",
                    "token",
                    null,
                    null,
                    "AGiorgetti",
                    "my-symphony",
                    ["Active", "Committed"],
                    ["Closed"])),
            NullLogger<AzureDevOpsIssueTrackerClient>.Instance);
    }

    private static string DecodeBasicCredential(string? encodedCredential)
    {
        Assert.False(string.IsNullOrWhiteSpace(encodedCredential));
        return Encoding.ASCII.GetString(Convert.FromBase64String(encodedCredential!));
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
        HttpMethod Method,
        Uri? RequestUri,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string Content)
    {
        public static async Task<CapturedRequest> CreateAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new CapturedRequest(
                request.Method,
                request.RequestUri,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                content);
        }
    }
}
