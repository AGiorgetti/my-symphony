using Microsoft.Extensions.Logging;
using Symphony.Application.Configuration;
using Symphony.Infrastructure.Configuration;
using Symphony.Infrastructure.Workflows;

namespace Symphony.Infrastructure.Tests.Workflows;

public sealed class WorkflowStartupValidationHostedServiceTests
{
    private static readonly SemaphoreSlim CurrentDirectoryGate = new(1, 1);

    [Fact]
    public async Task StartAsync_invalid_workflow_logs_actionable_error_and_throws()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"symphony-startup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(tempDirectory, "WORKFLOW.md"),
            """
            ---
            tracker:
              kind: [github
            ---
            Prompt
            """);

        var loggerProvider = new TestLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(loggerProvider));
        var service = new WorkflowStartupValidationHostedService(
            new WorkflowOptionsProvider(new YamlWorkflowLoader(), new WorkflowOptionsResolver()),
            loggerFactory.CreateLogger<WorkflowStartupValidationHostedService>());

        await CurrentDirectoryGate.WaitAsync();

        try
        {
            var previousDirectory = Directory.GetCurrentDirectory();

            try
            {
                Directory.SetCurrentDirectory(tempDirectory);
                var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(CancellationToken.None));

                Assert.Contains("workflow_parse_error", exception.Message);
                Assert.Contains("WORKFLOW.md", exception.Message);
                Assert.Contains(
                    loggerProvider.Messages,
                    message => message.Contains("Fix WORKFLOW.md before starting Symphony.", StringComparison.Ordinal));
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
    public async Task StartAsync_invalid_typed_config_logs_actionable_error_and_throws()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"symphony-startup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(tempDirectory, "WORKFLOW.md"),
            """
            ---
            tracker:
              kind: github
              repository: owner/repo
            ---
            Prompt
            """);

        var loggerProvider = new TestLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(loggerProvider));
        var service = new WorkflowStartupValidationHostedService(
            new WorkflowOptionsProvider(new YamlWorkflowLoader(), new WorkflowOptionsResolver()),
            loggerFactory.CreateLogger<WorkflowStartupValidationHostedService>());

        await CurrentDirectoryGate.WaitAsync();

        try
        {
            var previousDirectory = Directory.GetCurrentDirectory();

            try
            {
                Directory.SetCurrentDirectory(tempDirectory);
                var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(CancellationToken.None));

                Assert.Contains("missing_tracker_api_key", exception.Message);
                Assert.Contains("WORKFLOW.md", exception.Message);
                Assert.Contains(
                    loggerProvider.Messages,
                    message => message.Contains("Fix WORKFLOW.md before starting Symphony.", StringComparison.Ordinal));
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

    private sealed class TestLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName)
        {
            return new TestLogger(Messages);
        }

        public void Dispose()
        {
        }
    }

    private sealed class TestLogger(List<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
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
            messages.Add(formatter(state, exception));
        }
    }
}
