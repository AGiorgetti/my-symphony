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
        using var provider = new WorkflowOptionsProvider(new YamlWorkflowLoader(), new WorkflowOptionsResolver(), logger);

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

                var reloadedOptions = await provider.GetCurrentAsync();
                var reloadedDefinition = await provider.GetCurrentDefinitionAsync();

                Assert.Equal(125, initialOptions.Polling.IntervalMs);
                Assert.Equal("Initial prompt for {{ issue.identifier }}", initialDefinition.PromptTemplate);
                Assert.Equal(250, reloadedOptions.Polling.IntervalMs);
                Assert.Equal("Updated prompt for {{ issue.title }}", reloadedDefinition.PromptTemplate);
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
        using var provider = new WorkflowOptionsProvider(new YamlWorkflowLoader(), new WorkflowOptionsResolver(), logger);

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

                var currentOptions = await provider.GetCurrentAsync();
                var currentDefinition = await provider.GetCurrentDefinitionAsync();

                Assert.Equal(initialOptions, currentOptions);
                Assert.Equal(initialDefinition, currentDefinition);

                var errorEntry = logger.Entries.Last(
                    entry => entry.Message.Contains("workflow_reload failed", StringComparison.Ordinal));
                var errorCode = Assert.IsType<string>(errorEntry.State["error_code"]);
                Assert.True(
                    errorCode is "missing_tracker_api_key" or "workflow_parse_error",
                    $"Unexpected workflow reload error code '{errorCode}'.");
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
