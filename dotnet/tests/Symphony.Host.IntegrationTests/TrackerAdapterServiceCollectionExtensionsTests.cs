using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Symphony.Abstractions.Trackers;
using Symphony.Host.Composition;
using Symphony.Tracker.AzureDevOps;
using Symphony.Tracker.GitHub;
using Symphony.Tracker.Linear;

namespace Symphony.Host.IntegrationTests;

public class TrackerAdapterServiceCollectionExtensionsTests
{
    [Fact]
    public void AddConfiguredTrackerAdapter_registers_workflow_driven_selector_and_all_tracker_adapters()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITrackerClientOptionsProvider>(
            new StubTrackerClientOptionsProvider(
                new TrackerClientOptions(
                    TrackerAdapterKinds.GitHub,
                    "https://api.github.com",
                    "token",
                    "owner/repo",
                    null,
                    null,
                    null,
                    ["open"],
                    ["closed"])));
        var configuration = BuildConfiguration(null);

        services.AddConfiguredTrackerAdapter(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        var registrations = serviceProvider.GetServices<TrackerAdapterRegistration>().Select(static registration => registration.Kind).ToArray();

        Assert.IsType<WorkflowDrivenIssueTrackerClient>(serviceProvider.GetRequiredService<IIssueTrackerClient>());
        Assert.IsType<GitHubIssueTrackerClient>(serviceProvider.GetRequiredService<GitHubIssueTrackerClient>());
        Assert.IsType<AzureDevOpsIssueTrackerClient>(serviceProvider.GetRequiredService<AzureDevOpsIssueTrackerClient>());
        Assert.IsType<LinearIssueTrackerClient>(serviceProvider.GetRequiredService<LinearIssueTrackerClient>());
        Assert.Equal(
            [TrackerAdapterKinds.GitHub, TrackerAdapterKinds.AzureDevOps, TrackerAdapterKinds.Linear],
            registrations);
    }

    [Fact]
    public void AddConfiguredTrackerAdapter_does_not_require_host_tracker_kind_configuration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITrackerClientOptionsProvider>(
            new StubTrackerClientOptionsProvider(
                new TrackerClientOptions(
                    TrackerAdapterKinds.Linear,
                    "https://api.linear.app/graphql",
                    "token",
                    null,
                    "project-slug",
                    null,
                    null,
                    ["Todo"],
                    ["Done"])));
        var configuration = BuildConfiguration(null);

        services.AddConfiguredTrackerAdapter(configuration);

        using var serviceProvider = services.BuildServiceProvider();

        Assert.IsType<WorkflowDrivenIssueTrackerClient>(serviceProvider.GetRequiredService<IIssueTrackerClient>());
    }

    [Theory]
    [InlineData(TrackerAdapterKinds.GitHub, "#11")]
    [InlineData(TrackerAdapterKinds.AzureDevOps, "ADO-22")]
    [InlineData(TrackerAdapterKinds.Linear, "SYMP-33")]
    public async Task WorkflowDrivenIssueTrackerClient_routes_fetches_to_the_selected_tracker_kind(
        string trackerKind,
        string expectedIdentifier)
    {
        var optionsProvider = new StubTrackerClientOptionsProvider(BuildTrackerClientOptions(trackerKind));

        using var gitHubHttpClient = new HttpClient(
            new StaticResponseHandler(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        [
                          {
                            "id": 11,
                            "number": 11,
                            "title": "GitHub issue",
                            "state": "open",
                            "labels": [],
                            "created_at": "2026-03-16T08:00:00Z",
                            "updated_at": "2026-03-16T08:05:00Z"
                          }
                        ]
                        """,
                        Encoding.UTF8,
                        "application/json")
                }));
        using var azureDevOpsHttpClient = new HttpClient(
            new StaticResponseHandler(request =>
            {
                if (request.RequestUri?.AbsoluteUri.EndsWith("/_apis/wit/wiql?api-version=7.1", StringComparison.Ordinal) == true)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            """
                            {
                              "workItems": [
                                { "id": 22 }
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
                              "id": 22,
                              "fields": {
                                "System.Title": "Azure DevOps issue",
                                "System.State": "Active",
                                "System.CreatedDate": "2026-03-16T09:00:00Z",
                                "System.ChangedDate": "2026-03-16T09:05:00Z"
                              },
                              "relations": [],
                              "_links": {
                                "html": {
                                  "href": "https://dev.azure.com/org/project/_workitems/edit/22"
                                }
                              }
                            }
                          ]
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                };
            }));
        using var linearHttpClient = new HttpClient(
            new StaticResponseHandler(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "data": {
                            "issues": {
                              "nodes": [
                                {
                                  "id": "linear-33",
                                  "identifier": "SYMP-33",
                                  "title": "Linear issue",
                                  "description": "Linear description",
                                  "priority": 2,
                                  "branchName": "feature/symp-33",
                                  "url": "https://linear.app/acme/issue/SYMP-33",
                                  "createdAt": "2026-03-16T10:00:00Z",
                                  "updatedAt": "2026-03-16T10:05:00Z",
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
                }));

        var selector = new WorkflowDrivenIssueTrackerClient(
            optionsProvider,
            new GitHubIssueTrackerClient(gitHubHttpClient, optionsProvider, NullLogger<GitHubIssueTrackerClient>.Instance),
            new AzureDevOpsIssueTrackerClient(azureDevOpsHttpClient, optionsProvider, NullLogger<AzureDevOpsIssueTrackerClient>.Instance),
            new LinearIssueTrackerClient(linearHttpClient, optionsProvider, NullLogger<LinearIssueTrackerClient>.Instance));

        var issues = await selector.FetchCandidateIssuesAsync();

        var issue = Assert.Single(issues);
        Assert.Equal(expectedIdentifier, issue.Identifier);
    }

    private static IConfiguration BuildConfiguration(string? trackerKind)
    {
        var values = new Dictionary<string, string?>();
        if (trackerKind is not null)
        {
            values["tracker:kind"] = trackerKind;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private sealed class StubTrackerClientOptionsProvider(TrackerClientOptions options) : ITrackerClientOptionsProvider
    {
        public Task<TrackerClientOptions> GetCurrentAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(options);
        }
    }

    private static TrackerClientOptions BuildTrackerClientOptions(string trackerKind)
    {
        return trackerKind switch
        {
            TrackerAdapterKinds.GitHub => new TrackerClientOptions(
                trackerKind,
                "https://api.github.com",
                "token",
                "AGiorgetti/my-symphony",
                null,
                null,
                null,
                ["open"],
                ["closed"]),
            TrackerAdapterKinds.AzureDevOps => new TrackerClientOptions(
                trackerKind,
                "https://dev.azure.com",
                "token",
                null,
                null,
                "AGiorgetti",
                "my-symphony",
                ["Active"],
                ["Closed"]),
            TrackerAdapterKinds.Linear => new TrackerClientOptions(
                trackerKind,
                "https://api.linear.app/graphql",
                "token",
                null,
                "symphony",
                null,
                null,
                ["Todo"],
                ["Done"]),
            _ => throw new ArgumentOutOfRangeException(nameof(trackerKind))
        };
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> responseFactory;

        public StaticResponseHandler(HttpResponseMessage response)
            : this(_ => response)
        {
        }

        public StaticResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            this.responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }
}
