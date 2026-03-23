using Microsoft.Extensions.Logging;
using Symphony.Abstractions.Trackers;
using Symphony.Abstractions.Workspaces;
using Symphony.Application.Configuration;
using Symphony.Domain.Issues;
using Symphony.Domain.Workspaces;
using Symphony.Infrastructure.Startup;

namespace Symphony.Infrastructure.Tests.Startup;

public sealed class StartupTerminalWorkspaceCleanupHostedServiceTests
{
    [Fact]
    public async Task StartAsync_fetches_terminal_issues_and_deletes_matching_workspaces()
    {
        var trackerClient = new StubIssueTrackerClient
        {
            TerminalIssues =
            [
                CreateIssue("1", "ABC-1", "Done"),
                CreateIssue("2", "ABC-2", "Canceled"),
                CreateIssue("3", "ABC-1", "Done")
            ]
        };
        var workspaceManager = new StubWorkspaceManager();
        var logger = new TestLogger<StartupTerminalWorkspaceCleanupHostedService>();
        var service = CreateService(trackerClient, workspaceManager, logger);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(["Done", "Canceled"], trackerClient.LastFetchedStates);
        Assert.Equal(["ABC-1", "ABC-2"], workspaceManager.RequestedIssueIdentifiers);

        var completedEntry = Assert.Single(
            logger.Entries,
            entry => entry.Message.Contains("startup_terminal_cleanup completed", StringComparison.Ordinal));
        Assert.Equal(2d, Convert.ToDouble(completedEntry.State["terminal_issue_count"]));
        Assert.Equal(2d, Convert.ToDouble(completedEntry.State["cleaned_count"]));
        Assert.Equal(0d, Convert.ToDouble(completedEntry.State["failed_count"]));
    }

    [Fact]
    public async Task StartAsync_logs_warning_and_continues_when_terminal_issue_fetch_fails()
    {
        var trackerClient = new StubIssueTrackerClient
        {
            FetchByStatesException = new InvalidOperationException("boom")
        };
        var workspaceManager = new StubWorkspaceManager();
        var logger = new TestLogger<StartupTerminalWorkspaceCleanupHostedService>();
        var service = CreateService(trackerClient, workspaceManager, logger);

        await service.StartAsync(CancellationToken.None);

        Assert.Empty(workspaceManager.RequestedIssueIdentifiers);

        var warningEntry = Assert.Single(logger.Entries, entry => entry.LogLevel == LogLevel.Warning);
        Assert.Contains("reason=terminal_issue_fetch_failed", warningEntry.Message, StringComparison.Ordinal);
        Assert.NotNull(warningEntry.Exception);
    }

    [Fact]
    public async Task StartAsync_logs_warning_and_continues_when_workspace_cleanup_fails()
    {
        var trackerClient = new StubIssueTrackerClient
        {
            TerminalIssues =
            [
                CreateIssue("1", "ABC-1", "Done"),
                CreateIssue("2", "ABC-2", "Done")
            ]
        };
        var workspaceManager = new StubWorkspaceManager();
        workspaceManager.FailingIssueIdentifiers.Add("ABC-1");
        var logger = new TestLogger<StartupTerminalWorkspaceCleanupHostedService>();
        var service = CreateService(trackerClient, workspaceManager, logger);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(["ABC-1", "ABC-2"], workspaceManager.RequestedIssueIdentifiers);
        Assert.Equal(["ABC-2"], workspaceManager.DeletedIssueIdentifiers);

        var warningEntry = Assert.Single(logger.Entries, entry => entry.LogLevel == LogLevel.Warning);
        Assert.Contains("reason=workspace_cleanup_failed", warningEntry.Message, StringComparison.Ordinal);

        var completedEntry = Assert.Single(
            logger.Entries,
            entry => entry.Message.Contains("startup_terminal_cleanup completed", StringComparison.Ordinal));
        Assert.Equal(2d, Convert.ToDouble(completedEntry.State["terminal_issue_count"]));
        Assert.Equal(1d, Convert.ToDouble(completedEntry.State["cleaned_count"]));
        Assert.Equal(1d, Convert.ToDouble(completedEntry.State["failed_count"]));
    }

    private static StartupTerminalWorkspaceCleanupHostedService CreateService(
        IIssueTrackerClient trackerClient,
        StubWorkspaceManager workspaceManager,
        ILogger<StartupTerminalWorkspaceCleanupHostedService> logger)
    {
        return new StartupTerminalWorkspaceCleanupHostedService(
            new StaticWorkflowOptionsProvider(CreateWorkflowOptions()),
            trackerClient,
            workspaceManager,
            logger);
    }

    private static WorkflowServiceOptions CreateWorkflowOptions()
    {
        return new WorkflowServiceOptions(
            new WorkflowTrackerOptions(
                "github",
                "https://api.github.com",
                "token",
                null,
                "owner/repo",
                null,
                null,
                ["Todo", "In Progress"],
                ["Done", "Canceled"]),
            new WorkflowPollingOptions(1_000),
            new WorkflowWorkspaceOptions(Path.Combine(Path.GetTempPath(), "symphony-tests")),
            new WorkflowHookOptions(
                null,
                null,
                null,
                null,
                60_000),
            new WorkflowAgentOptions(
                4,
                20,
                300_000,
                new Dictionary<string, int>(StringComparer.Ordinal),
                false,
                "exec:agent"),
            new WorkflowCodexOptions(
                "codex app-server",
                null,
                null,
                null,
                3_600_000,
                5_000,
                300_000));
    }

    private static Issue CreateIssue(string id, string identifier, string state)
    {
        return new Issue(
            id,
            identifier,
            $"Issue {identifier}",
            description: "Startup cleanup test",
            priority: 1,
            state: state,
            createdAt: new DateTimeOffset(2026, 3, 23, 10, 0, 0, TimeSpan.Zero));
    }

    private sealed class StaticWorkflowOptionsProvider(WorkflowServiceOptions workflowOptions) : IWorkflowOptionsProvider
    {
        public Task<WorkflowServiceOptions> GetCurrentAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(workflowOptions);
        }
    }

    private sealed class StubIssueTrackerClient : IIssueTrackerClient
    {
        public IReadOnlyList<Issue> TerminalIssues { get; set; } = Array.Empty<Issue>();

        public Exception? FetchByStatesException { get; set; }

        public IReadOnlyList<string> LastFetchedStates { get; private set; } = Array.Empty<string>();

        public Task<IReadOnlyList<Issue>> FetchCandidateIssuesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Issue>>(Array.Empty<Issue>());
        }

        public Task<IReadOnlyList<Issue>> FetchIssuesByStatesAsync(
            IReadOnlyCollection<string> stateNames,
            CancellationToken cancellationToken = default)
        {
            if (FetchByStatesException is not null)
            {
                throw FetchByStatesException;
            }

            LastFetchedStates = stateNames.ToArray();
            return Task.FromResult(TerminalIssues);
        }

        public Task<IReadOnlyList<Issue>> FetchIssueStatesByIdsAsync(
            IReadOnlyCollection<string> issueIds,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Issue>>(Array.Empty<Issue>());
        }
    }

    private sealed class StubWorkspaceManager : IWorkspaceManager
    {
        public List<string> RequestedIssueIdentifiers { get; } = [];

        public List<string> DeletedIssueIdentifiers { get; } = [];

        public HashSet<string> FailingIssueIdentifiers { get; } = new(StringComparer.Ordinal);

        public Task<Workspace> CreateForIssueAsync(string issueIdentifier, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new Workspace(Path.Combine(Path.GetTempPath(), issueIdentifier), issueIdentifier, createdNow: true));
        }

        public Task DeleteForIssueAsync(string issueIdentifier, CancellationToken cancellationToken = default)
        {
            RequestedIssueIdentifiers.Add(issueIdentifier);

            if (FailingIssueIdentifiers.Contains(issueIdentifier))
            {
                throw new InvalidOperationException($"Failed to delete {issueIdentifier}.");
            }

            DeletedIssueIdentifiers.Add(issueIdentifier);
            return Task.CompletedTask;
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<TestLogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var structuredState = state as IEnumerable<KeyValuePair<string, object?>>;
            var values = structuredState?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                ?? new Dictionary<string, object?>(StringComparer.Ordinal);

            Entries.Add(new TestLogEntry(logLevel, formatter(state, exception), values, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed record TestLogEntry(
        LogLevel LogLevel,
        string Message,
        IReadOnlyDictionary<string, object?> State,
        Exception? Exception);
}
