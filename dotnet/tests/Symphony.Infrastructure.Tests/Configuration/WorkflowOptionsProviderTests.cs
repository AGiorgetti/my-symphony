using Microsoft.Extensions.Logging;
using Symphony.Application.Configuration;
using Symphony.Domain.Workflows;
using Symphony.Infrastructure.Configuration;
using Symphony.Infrastructure.Workflows;

namespace Symphony.Infrastructure.Tests.Configuration;

public sealed class WorkflowOptionsProviderTests
{
    private static readonly SemaphoreSlim CurrentDirectoryGate = new(1, 1);
    private static long _lastWriteSequence;

    [Fact]
    public async Task GetCurrentAsync_reloads_workflow_options_and_prompt_after_file_change()
    {
        var tempDirectory = CreateTemporaryDirectory();
        var workflowPath = Path.Combine(tempDirectory, "WORKFLOW.md");
        await WriteWorkflowFileAsync(
            workflowPath,
            """
            ---
            tracker:
              kind: github
              api_key: token
              repository: AGiorgetti/my-symphony
            polling:
              interval_ms: 125
            ---
            Initial prompt for {{ issue.identifier }}
            """);

        var logger = new TestLogger<WorkflowOptionsProvider>();
        var workflowLoadStatusTracker = new WorkflowLoadStatusTracker();
        using var provider = CreateProvider(logger, workflowLoadStatusTracker);

        await CurrentDirectoryGate.WaitAsync();

        try
        {
            var previousDirectory = Directory.GetCurrentDirectory();

            try
            {
                Directory.SetCurrentDirectory(tempDirectory);

                var initialOptions = await provider.GetCurrentAsync();
                var initialDefinition = await provider.GetCurrentDefinitionAsync();

                await WriteWorkflowFileAsync(
                    workflowPath,
                    """
                    ---
                    tracker:
                      kind: github
                      api_key: token
                      repository: AGiorgetti/my-symphony
                    polling:
                      interval_ms: 250
                    ---
                    Updated prompt for {{ issue.title }}
                    """);

                var reloadedSnapshot = await WaitForReloadAsync(provider, workflowLoadStatusTracker);

                Assert.Equal(125, initialOptions.Polling.IntervalMs);
                Assert.Equal("Initial prompt for {{ issue.identifier }}", initialDefinition.PromptTemplate);
                Assert.Equal(250, reloadedSnapshot.Options.Polling.IntervalMs);
                Assert.Equal("Updated prompt for {{ issue.title }}", reloadedSnapshot.Definition.PromptTemplate);
                Assert.Equal("Loaded", workflowLoadStatusTracker.GetSnapshot().Status);
                Assert.Equal(250, workflowLoadStatusTracker.GetSnapshot().PollingIntervalMs);
                Assert.Contains(
                    logger.Entries,
                    entry => entry.Message.Contains("workflow_reload completed", StringComparison.Ordinal));
            }
            finally
            {
                Directory.SetCurrentDirectory(previousDirectory);
            }
        }
        finally
        {
            CurrentDirectoryGate.Release();
        }
    }

    [Fact]
    public async Task GetCurrentAsync_invalid_reload_keeps_last_known_good_and_logs_error()
    {
        var tempDirectory = CreateTemporaryDirectory();
        var workflowPath = Path.Combine(tempDirectory, "WORKFLOW.md");
        await WriteWorkflowFileAsync(
            workflowPath,
            """
            ---
            tracker:
              kind: github
              api_key: token
              repository: AGiorgetti/my-symphony
            polling:
              interval_ms: 125
            ---
            Initial prompt for {{ issue.identifier }}
            """);

        var logger = new TestLogger<WorkflowOptionsProvider>();
        var workflowLoadStatusTracker = new WorkflowLoadStatusTracker();
        using var provider = CreateProvider(logger, workflowLoadStatusTracker);

        await CurrentDirectoryGate.WaitAsync();

        try
        {
            var previousDirectory = Directory.GetCurrentDirectory();

            try
            {
                Directory.SetCurrentDirectory(tempDirectory);

                var initialOptions = await provider.GetCurrentAsync();
                var initialDefinition = await provider.GetCurrentDefinitionAsync();

                await WriteWorkflowFileAsync(
                    workflowPath,
                    """
                    ---
                    tracker:
                      kind: github
                      repository: AGiorgetti/my-symphony
                    polling:
                      interval_ms: 500
                    ---
                    Broken prompt
                    """);

                var failedReload = await WaitForFailedReloadAsync(provider, workflowLoadStatusTracker);

                Assert.Equal(initialOptions, failedReload.Options);
                Assert.Equal(initialDefinition, failedReload.Definition);

                var errorEntry = logger.Entries.Last(
                    entry => entry.Message.Contains("workflow_reload failed", StringComparison.Ordinal));
                var errorCode = Assert.IsType<string>(errorEntry.State["error_code"]);
                Assert.True(
                    errorCode is "missing_tracker_api_key" or "workflow_parse_error" or "missing_workflow_file",
                    $"Unexpected workflow reload error code '{errorCode}'.");
                var snapshot = failedReload.Snapshot;
                Assert.Equal("ReloadFailedUsingLastKnownGood", snapshot.Status);
                Assert.Equal(initialOptions.Polling.IntervalMs, snapshot.PollingIntervalMs);
                Assert.Equal(errorCode, snapshot.LastErrorCode);
                Assert.NotNull(snapshot.LastSuccessfulLoadAt);
            }
            finally
            {
                Directory.SetCurrentDirectory(previousDirectory);
            }
        }
        finally
        {
            CurrentDirectoryGate.Release();
        }
    }

    private static WorkflowOptionsProvider CreateProvider(
        TestLogger<WorkflowOptionsProvider> logger,
        WorkflowLoadStatusTracker workflowLoadStatusTracker)
    {
        return new WorkflowOptionsProvider(
            new YamlWorkflowLoader(),
            new WorkflowOptionsResolver(),
            workflowLoadStatusTracker,
            TimeProvider.System,
            logger);
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"symphony-provider-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task WriteWorkflowFileAsync(string workflowPath, string content)
    {
        var temporaryPath = $"{workflowPath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporaryPath, content);
        File.Move(temporaryPath, workflowPath, overwrite: true);
        File.SetLastWriteTimeUtc(
            workflowPath,
            DateTime.UtcNow.AddSeconds(Interlocked.Increment(ref _lastWriteSequence)));
    }

    private static async Task<(WorkflowServiceOptions Options, WorkflowDefinition Definition)> WaitForReloadAsync(
        WorkflowOptionsProvider provider,
        WorkflowLoadStatusTracker workflowLoadStatusTracker)
    {
        var deadline = TimeProvider.System.GetUtcNow() + TimeSpan.FromSeconds(5);
        WorkflowServiceOptions? latestOptions = null;
        WorkflowDefinition? latestDefinition = null;

        while (TimeProvider.System.GetUtcNow() < deadline)
        {
            var options = await provider.GetCurrentAsync();
            var definition = await provider.GetCurrentDefinitionAsync();
            var snapshot = workflowLoadStatusTracker.GetSnapshot();
            latestOptions = options;
            latestDefinition = definition;

            if (options.Polling.IntervalMs == 250
                && string.Equals(
                    definition.PromptTemplate,
                    "Updated prompt for {{ issue.title }}",
                    StringComparison.Ordinal)
                && string.Equals(snapshot.Status, "Loaded", StringComparison.Ordinal)
                && snapshot.PollingIntervalMs == 250)
            {
                return (options, definition);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        var finalOptions = latestOptions ?? await provider.GetCurrentAsync();
        var finalDefinition = latestDefinition ?? await provider.GetCurrentDefinitionAsync();
        var finalSnapshot = workflowLoadStatusTracker.GetSnapshot();

        throw new Xunit.Sdk.XunitException(
            $"Timed out waiting for workflow reload. Last observed interval was {finalOptions.Polling.IntervalMs}, " +
            $"last prompt was '{finalDefinition.PromptTemplate}', last status was '{finalSnapshot.Status}', " +
            $"last tracked interval was {finalSnapshot.PollingIntervalMs?.ToString() ?? "<null>"}.");
    }

    private static async Task<(
        WorkflowServiceOptions Options,
        WorkflowDefinition Definition,
        WorkflowLoadStatusSnapshot Snapshot)> WaitForFailedReloadAsync(
        WorkflowOptionsProvider provider,
        WorkflowLoadStatusTracker workflowLoadStatusTracker)
    {
        var deadline = TimeProvider.System.GetUtcNow() + TimeSpan.FromSeconds(5);
        WorkflowServiceOptions? latestOptions = null;
        WorkflowDefinition? latestDefinition = null;
        WorkflowLoadStatusSnapshot? latestSnapshot = null;

        while (TimeProvider.System.GetUtcNow() < deadline)
        {
            var options = await provider.GetCurrentAsync();
            var definition = await provider.GetCurrentDefinitionAsync();
            var snapshot = workflowLoadStatusTracker.GetSnapshot();
            latestOptions = options;
            latestDefinition = definition;
            latestSnapshot = snapshot;

            if (string.Equals(snapshot.Status, "ReloadFailedUsingLastKnownGood", StringComparison.Ordinal))
            {
                return (options, definition, snapshot);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        return (
            latestOptions ?? await provider.GetCurrentAsync(),
            latestDefinition ?? await provider.GetCurrentDefinitionAsync(),
            latestSnapshot ?? workflowLoadStatusTracker.GetSnapshot());
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<TestLogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
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
    }

    private sealed record TestLogEntry(
        LogLevel LogLevel,
        string Message,
        IReadOnlyDictionary<string, object?> State,
        Exception? Exception);

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
