using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Symphony.Abstractions.Trackers;
using Symphony.Abstractions.Workspaces;
using Symphony.Application.Configuration;
using Symphony.Application.Orchestration;
using Symphony.Domain.Issues;
using Symphony.Domain.Runs;
using Symphony.Domain.Workspaces;
using Symphony.Host.Composition;
using Symphony.Host.Dashboard;
using Symphony.Host.Theming;

namespace Symphony.Host.IntegrationTests;

public sealed class SymphonyHostLifecycleIntegrationTests
{
    private static readonly SemaphoreSlim CurrentDirectoryGate = new(1, 1);

    [Fact]
    public async Task StartAsync_missing_default_workflow_fails_cleanly()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => StartedSymphonyHost.StartAsync(_ => null));

        Assert.Contains("missing_workflow_file", exception.Message, StringComparison.Ordinal);
        Assert.Contains("WORKFLOW.md", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_uses_workflow_server_port_when_configured()
    {
        await using var host = await StartedSymphonyHost.StartAsync(
            tempDirectory => CreateWorkflowContents(
                Path.Combine(tempDirectory, "workspaces"),
                serverSection:
                """
                server:
                  port: 0
                """),
            configureServices: services =>
            {
                services.RemoveAll<IIssueTrackerClient>();
                services.AddSingleton<IIssueTrackerClient>(new EmptyIssueTrackerClient());

                RemoveHostedService<RetryDispatchBackgroundService>(services);
            },
            useEphemeralLoopbackUrl: false);

        var address = GetServerAddress(host.App);

        Assert.Equal("127.0.0.1", address.Host);
        Assert.True(address.Port > 0);
    }

    [Fact]
    public async Task StartAsync_cli_port_overrides_workflow_server_port()
    {
        await using var host = await StartedSymphonyHost.StartAsync(
            tempDirectory => CreateWorkflowContents(
                Path.Combine(tempDirectory, "workspaces"),
                serverSection:
                """
                server:
                  port: nope
                """),
            configureServices: services =>
            {
                services.RemoveAll<IIssueTrackerClient>();
                services.AddSingleton<IIssueTrackerClient>(new EmptyIssueTrackerClient());

                RemoveHostedService<RetryDispatchBackgroundService>(services);
            },
            args: ["--port", "0"],
            useEphemeralLoopbackUrl: false);

        var address = GetServerAddress(host.App);

        Assert.Equal("127.0.0.1", address.Host);
        Assert.True(address.Port > 0);
    }

    [Fact]
    public async Task StartAsync_invalid_workflow_server_port_fails_cleanly()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => StartedSymphonyHost.StartAsync(
                tempDirectory => CreateWorkflowContents(
                    Path.Combine(tempDirectory, "workspaces"),
                    serverSection:
                    """
                    server:
                      port: nope
                    """),
                configureServices: null,
                useEphemeralLoopbackUrl: false));

        Assert.Contains("invalid_server_port", exception.Message, StringComparison.Ordinal);
        Assert.Contains("server.port", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_invalid_cli_port_fails_cleanly()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => StartedSymphonyHost.StartAsync(
                tempDirectory => CreateWorkflowContents(Path.Combine(tempDirectory, "workspaces")),
                configureServices: null,
                args: ["--port", "nope"],
                useEphemeralLoopbackUrl: false));

        Assert.Contains("invalid_cli_port", exception.Message, StringComparison.Ordinal);
        Assert.Contains("--port", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_runs_smoke_flow_from_workflow_load_to_workspace_create_and_worker_attempt()
    {
        var issue = new Issue(
            id: "42",
            identifier: "ABC-42",
            title: "Smoke flow",
            description: "Validate the orchestration path",
            priority: 1,
            state: "Todo",
            createdAt: new DateTimeOffset(2026, 3, 18, 9, 0, 0, TimeSpan.Zero));
        var trackerClient = new SmokeIssueTrackerClient(issue);
        var probe = new SmokeRunProbe();

        await using var host = await StartedSymphonyHost.StartAsync(
            tempDirectory => CreateWorkflowContents(Path.Combine(tempDirectory, "workspaces")),
            configureBuilder: builder =>
            {
                builder.Configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Orchestration:InitialState"] = "Started"
                    });
            },
            configureServices: services =>
            {
                services.RemoveAll<IIssueTrackerClient>();
                services.AddSingleton<IIssueTrackerClient>(trackerClient);

                services.RemoveAll<IQueuedIssueWorker>();
                services.AddSingleton(probe);
                services.AddSingleton<IQueuedIssueWorker, SmokeQueuedIssueWorker>();

                RemoveHostedService<RetryDispatchBackgroundService>(services);
            });

        var observation = await probe.AttemptObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var expectedWorkspacePath = Path.GetFullPath(Path.Combine(host.TempDirectory, "workspaces", "ABC-42"));

        Assert.True(trackerClient.FetchCandidateIssuesCallCount >= 1);
        Assert.Equal("ABC-42", observation.IssueIdentifier);
        Assert.Null(observation.Attempt);
        Assert.Equal("Smoke prompt for {{ issue.identifier }} on attempt {{ attempt }}.", observation.PromptTemplate);
        Assert.Equal(expectedWorkspacePath, observation.Workspace.Path);
        Assert.Equal("ABC-42", observation.Workspace.WorkspaceKey);
        Assert.True(observation.Workspace.CreatedNow);
        Assert.True(Directory.Exists(expectedWorkspacePath));
    }

    [Fact]
    public async Task StartAsync_runs_terminal_workspace_cleanup_before_polling_dispatch()
    {
        var activeIssue = new Issue(
            id: "42",
            identifier: "ABC-42",
            title: "Smoke flow",
            description: "Validate the orchestration path",
            priority: 1,
            state: "Todo",
            createdAt: new DateTimeOffset(2026, 3, 18, 9, 0, 0, TimeSpan.Zero));
        var terminalIssue = new Issue(
            id: "9",
            identifier: "DONE-9",
            title: "Completed",
            description: "Cleanup target",
            priority: 1,
            state: "Done",
            createdAt: new DateTimeOffset(2026, 3, 17, 9, 0, 0, TimeSpan.Zero));
        var trackerClient = new StartupCleanupTrackerClient(activeIssue, terminalIssue);
        var probe = new SmokeRunProbe();
        var staleWorkspacePath = string.Empty;

        await using var host = await StartedSymphonyHost.StartAsync(
            tempDirectory =>
            {
                staleWorkspacePath = Path.Combine(tempDirectory, "workspaces", terminalIssue.Identifier);
                Directory.CreateDirectory(staleWorkspacePath);
                return CreateWorkflowContents(Path.Combine(tempDirectory, "workspaces"));
            },
            configureBuilder: builder =>
            {
                builder.Configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Orchestration:InitialState"] = "Started"
                    });
            },
            configureServices: services =>
            {
                services.RemoveAll<IIssueTrackerClient>();
                services.AddSingleton<IIssueTrackerClient>(trackerClient);

                services.RemoveAll<IQueuedIssueWorker>();
                services.AddSingleton(probe);
                services.AddSingleton<IQueuedIssueWorker, SmokeQueuedIssueWorker>();

                RemoveHostedService<RetryDispatchBackgroundService>(services);
            });

        await probe.AttemptObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(Directory.Exists(staleWorkspacePath));
        Assert.Equal(["Done"], trackerClient.LastFetchedStates);
        Assert.Equal("terminal_fetch", trackerClient.CallOrder[0]);
        Assert.Contains("candidate_fetch", trackerClient.CallOrder);
        Assert.True(
            trackerClient.CallOrder.IndexOf("terminal_fetch") < trackerClient.CallOrder.IndexOf("candidate_fetch"),
            "Startup terminal cleanup should run before polling fetches candidates.");
    }

    [Fact]
    public async Task StartAsync_exposes_ui_routes_and_ui_services_from_di()
    {
        await using var host = await StartedSymphonyHost.StartAsync(
            tempDirectory => CreateWorkflowContents(Path.Combine(tempDirectory, "workspaces")),
            services =>
            {
                services.RemoveAll<IIssueTrackerClient>();
                services.AddSingleton<IIssueTrackerClient>(new EmptyIssueTrackerClient());

                RemoveHostedService<RetryDispatchBackgroundService>(services);
            });
        using var client = CreateHttpClient(host.App);
        using var scope = host.App.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetService<ISessionActivityStore>());
        Assert.NotNull(scope.ServiceProvider.GetService<IThemeService>());

        var rootResponse = await client.GetAsync("/");
        var sessionsResponse = await client.GetAsync("/sessions");
        var unknownSessionResponse = await client.GetAsync("/sessions/nonexistent-id");

        Assert.Equal(System.Net.HttpStatusCode.OK, rootResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, sessionsResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, unknownSessionResponse.StatusCode);
    }

    private static string CreateWorkflowContents(string workspaceRoot, string? serverSection = null)
    {
        var normalizedWorkspaceRoot = workspaceRoot.Replace('\\', '/');
        var serverBlock = serverSection is null
            ? string.Empty
            : serverSection.TrimEnd() + Environment.NewLine;

        return
            "---" + Environment.NewLine
            + serverBlock
            + $$"""
            tracker:
              kind: github
              api_key: test-token
              repository: AGiorgetti/my-symphony
              active_states:
                - Todo
              terminal_states:
                - Done
            polling:
              interval_ms: 60000
            workspace:
              root: "{{normalizedWorkspaceRoot}}"
            agent:
              max_concurrent_agents: 1
              max_turns: 5
            codex:
              command: codex app-server
            ---
            """
            + Environment.NewLine
            + "Smoke prompt for {{ issue.identifier }} on attempt {{ attempt }}.";
    }

    private static HttpClient CreateHttpClient(WebApplication app)
    {
        return new HttpClient
        {
            BaseAddress = GetServerAddress(app)
        };
    }

    private static Uri GetServerAddress(WebApplication app)
    {
        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Test server addresses are unavailable.");
        var address = Assert.Single(addresses.Addresses);

        return new Uri(address);
    }

    private static void RemoveHostedService<TImplementation>(IServiceCollection services)
        where TImplementation : class, IHostedService
    {
        for (var index = services.Count - 1; index >= 0; index--)
        {
            var descriptor = services[index];
            if (descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(TImplementation))
            {
                services.RemoveAt(index);
            }
        }
    }

    private sealed class StartedSymphonyHost : IAsyncDisposable
    {
        private readonly string _previousDirectory;
        private int _disposed;

        private StartedSymphonyHost(WebApplication app, string tempDirectory, string previousDirectory)
        {
            App = app;
            TempDirectory = tempDirectory;
            _previousDirectory = previousDirectory;
        }

        public WebApplication App { get; }

        public string TempDirectory { get; }

        public static Task<StartedSymphonyHost> StartAsync(
            Func<string, string?> workflowFactory,
            Action<IServiceCollection>? configureServices)
        {
            return StartAsync(workflowFactory, configureBuilder: null, configureServices);
        }

        public static async Task<StartedSymphonyHost> StartAsync(
            Func<string, string?> workflowFactory,
            Action<WebApplicationBuilder>? configureBuilder = null,
            Action<IServiceCollection>? configureServices = null,
            string[]? args = null,
            bool useEphemeralLoopbackUrl = true)
        {
            ArgumentNullException.ThrowIfNull(workflowFactory);

            var tempDirectory = Path.Combine(Path.GetTempPath(), $"symphony-host-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            var workflowContents = workflowFactory(tempDirectory);
            if (workflowContents is not null)
            {
                await File.WriteAllTextAsync(
                    Path.Combine(tempDirectory, "WORKFLOW.md"),
                    workflowContents).ConfigureAwait(false);
            }

            await CurrentDirectoryGate.WaitAsync().ConfigureAwait(false);
            var previousDirectory = Directory.GetCurrentDirectory();
            WebApplication? app = null;

            try
            {
                Directory.SetCurrentDirectory(tempDirectory);

                var builder = WebApplication.CreateBuilder(args ?? Array.Empty<string>());
                if (useEphemeralLoopbackUrl)
                {
                    builder.WebHost.UseUrls("http://127.0.0.1:0");
                }

                configureBuilder?.Invoke(builder);
                builder.AddSymphonyHost();
                configureServices?.Invoke(builder.Services);

                app = builder.Build();
                app.MapSymphonyHost();
                await app.StartAsync().ConfigureAwait(false);

                return new StartedSymphonyHost(app, tempDirectory, previousDirectory);
            }
            catch
            {
                if (app is not null)
                {
                    await app.DisposeAsync().ConfigureAwait(false);
                }

                Directory.SetCurrentDirectory(previousDirectory);
                CurrentDirectoryGate.Release();
                TryDeleteDirectory(tempDirectory);
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                await App.StopAsync().ConfigureAwait(false);
                await App.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                Directory.SetCurrentDirectory(_previousDirectory);
                CurrentDirectoryGate.Release();
                TryDeleteDirectory(TempDirectory);
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class SmokeQueuedIssueWorker(
        SmokeRunProbe probe,
        IWorkflowDefinitionProvider workflowDefinitionProvider,
        IWorkspaceManager workspaceManager) : IQueuedIssueWorker
    {
        public async Task ExecuteAsync(QueuedIssueExecutionContext context)
        {
            context.UpdateStatus(RunAttemptStatus.PreparingWorkspace);
            var workspace = await workspaceManager.CreateForIssueAsync(context.Issue.Identifier, context.CancellationToken).ConfigureAwait(false);

            context.UpdateStatus(RunAttemptStatus.BuildingPrompt);
            var workflowDefinition = await workflowDefinitionProvider.GetCurrentDefinitionAsync(context.CancellationToken).ConfigureAwait(false);

            context.UpdateStatus(RunAttemptStatus.LaunchingAgentProcess);
            probe.Record(
                new SmokeRunObservation(
                    context.Issue.Identifier,
                    context.Attempt,
                    workflowDefinition.PromptTemplate,
                    workspace));

            context.UpdateStatus(RunAttemptStatus.Finishing);
        }
    }

    private sealed class SmokeIssueTrackerClient(Issue issue) : IIssueTrackerClient
    {
        private readonly IReadOnlyList<Issue> _issues = [issue];
        private int _fetchCandidateIssuesCallCount;

        public int FetchCandidateIssuesCallCount => Volatile.Read(ref _fetchCandidateIssuesCallCount);

        public Task<IReadOnlyList<Issue>> FetchCandidateIssuesAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _fetchCandidateIssuesCallCount);
            return Task.FromResult(_issues);
        }

        public Task<IReadOnlyList<Issue>> FetchIssuesByStatesAsync(
            IReadOnlyCollection<string> stateNames,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Issue>>(Array.Empty<Issue>());
        }

        public Task<IReadOnlyList<Issue>> FetchIssueStatesByIdsAsync(
            IReadOnlyCollection<string> issueIds,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Issue>>(Array.Empty<Issue>());
        }
    }

    private sealed class StartupCleanupTrackerClient(Issue activeIssue, Issue terminalIssue) : IIssueTrackerClient
    {
        public List<string> CallOrder { get; } = [];

        public IReadOnlyList<string> LastFetchedStates { get; private set; } = Array.Empty<string>();

        public Task<IReadOnlyList<Issue>> FetchCandidateIssuesAsync(CancellationToken cancellationToken = default)
        {
            CallOrder.Add("candidate_fetch");
            return Task.FromResult<IReadOnlyList<Issue>>([activeIssue]);
        }

        public Task<IReadOnlyList<Issue>> FetchIssuesByStatesAsync(
            IReadOnlyCollection<string> stateNames,
            CancellationToken cancellationToken = default)
        {
            CallOrder.Add("terminal_fetch");
            LastFetchedStates = stateNames.ToArray();
            return Task.FromResult<IReadOnlyList<Issue>>([terminalIssue]);
        }

        public Task<IReadOnlyList<Issue>> FetchIssueStatesByIdsAsync(
            IReadOnlyCollection<string> issueIds,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Issue>>(Array.Empty<Issue>());
        }
    }

    private sealed class EmptyIssueTrackerClient : IIssueTrackerClient
    {
        public Task<IReadOnlyList<Issue>> FetchCandidateIssuesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Issue>>(Array.Empty<Issue>());
        }

        public Task<IReadOnlyList<Issue>> FetchIssuesByStatesAsync(
            IReadOnlyCollection<string> stateNames,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Issue>>(Array.Empty<Issue>());
        }

        public Task<IReadOnlyList<Issue>> FetchIssueStatesByIdsAsync(
            IReadOnlyCollection<string> issueIds,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Issue>>(Array.Empty<Issue>());
        }
    }

    private sealed class SmokeRunProbe
    {
        public TaskCompletionSource<SmokeRunObservation> AttemptObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Record(SmokeRunObservation observation)
        {
            AttemptObserved.TrySetResult(observation);
        }
    }

    private sealed record SmokeRunObservation(
        string IssueIdentifier,
        int? Attempt,
        string PromptTemplate,
        Workspace Workspace);
}
