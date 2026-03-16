using Microsoft.Extensions.Logging.Abstractions;
using Symphony.Application.Configuration;
using Symphony.Application.Polling;
using Symphony.Application.Tests.Logging;

namespace Symphony.Application.Tests.Polling;

public sealed class PollingBackgroundServiceTests
{
    [Fact]
    public async Task StartAsync_uses_poll_interval_from_typed_config()
    {
        var firstTick = new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondTick = new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationCount = 0;
        var service = new PollingBackgroundService(
            new StaticWorkflowOptionsProvider(CreateWorkflowOptions(intervalMs: 80)),
            new DelegatePollingIterationHandler(
                (_, _) =>
                {
                    var now = DateTimeOffset.UtcNow;
                    var currentCount = Interlocked.Increment(ref invocationCount);

                    if (currentCount == 1)
                    {
                        firstTick.TrySetResult(now);
                    }
                    else if (currentCount == 2)
                    {
                        secondTick.TrySetResult(now);
                    }

                    return Task.CompletedTask;
                }),
            new PollingRefreshTrigger(TimeProvider.System),
            TimeProvider.System,
            NullLogger<PollingBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);

        var firstTickTimestamp = await firstTick.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var secondTickTimestamp = await secondTick.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await service.StopAsync(CancellationToken.None);

        Assert.True(
            secondTickTimestamp - firstTickTimestamp >= TimeSpan.FromMilliseconds(50),
            "The polling loop should wait for the typed workflow polling interval before the next tick.");
    }

    [Fact]
    public async Task StopAsync_cancels_active_iteration_and_waits_for_completion()
    {
        var iterationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capturedToken = CancellationToken.None;
        var service = new PollingBackgroundService(
            new StaticWorkflowOptionsProvider(CreateWorkflowOptions(intervalMs: 1_000)),
            new DelegatePollingIterationHandler(
                async (_, cancellationToken) =>
                {
                    capturedToken = cancellationToken;
                    iterationStarted.TrySetResult();

                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        cancellationObserved.TrySetResult();
                        throw;
                    }
                }),
            new PollingRefreshTrigger(TimeProvider.System),
            TimeProvider.System,
            NullLogger<PollingBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await iterationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await service.StopAsync(stopCts.Token);
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(capturedToken.CanBeCanceled);
    }

    [Fact]
    public async Task StartAsync_logs_polling_lifecycle_with_structured_interval()
    {
        var firstInvocation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var logger = new TestLogger<PollingBackgroundService>();
        var service = new PollingBackgroundService(
            new StaticWorkflowOptionsProvider(CreateWorkflowOptions(intervalMs: 75)),
            new DelegatePollingIterationHandler(
                (_, _) =>
                {
                    firstInvocation.TrySetResult();
                    return Task.CompletedTask;
                }),
            new PollingRefreshTrigger(TimeProvider.System),
            TimeProvider.System,
            logger);

        await service.StartAsync(CancellationToken.None);
        await firstInvocation.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync(CancellationToken.None);

        var startEntry = Assert.Single(
            logger.Entries,
            entry => entry.Message.Contains("poll_tick started", StringComparison.Ordinal));
        var completedEntry = Assert.Single(
            logger.Entries,
            entry => entry.Message.Contains("poll_tick completed", StringComparison.Ordinal));

        Assert.Equal(75, Assert.IsType<int>(startEntry.State["poll_interval_ms"]));
        Assert.Equal(75, Assert.IsType<int>(completedEntry.State["poll_interval_ms"]));
        Assert.Contains(
            logger.Entries,
            entry => entry.Message.Contains("polling_service completed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RequestRefresh_wakes_the_polling_loop_before_the_interval_elapses()
    {
        var firstInvocation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondInvocation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationCount = 0;
        var refreshTrigger = new PollingRefreshTrigger(TimeProvider.System);
        var service = new PollingBackgroundService(
            new StaticWorkflowOptionsProvider(CreateWorkflowOptions(intervalMs: 30_000)),
            new DelegatePollingIterationHandler(
                (_, _) =>
                {
                    var currentInvocation = Interlocked.Increment(ref invocationCount);
                    if (currentInvocation == 1)
                    {
                        firstInvocation.TrySetResult();
                    }
                    else if (currentInvocation == 2)
                    {
                        secondInvocation.TrySetResult();
                    }

                    return Task.CompletedTask;
                }),
            refreshTrigger,
            TimeProvider.System,
            NullLogger<PollingBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await firstInvocation.Task.WaitAsync(TimeSpan.FromSeconds(2));

        refreshTrigger.RequestRefresh();
        await secondInvocation.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(2, invocationCount);
    }

    private static WorkflowServiceOptions CreateWorkflowOptions(int intervalMs)
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
                ["Todo"],
                ["Done"]),
            new WorkflowPollingOptions(intervalMs),
            new WorkflowWorkspaceOptions(Path.Combine(Path.GetTempPath(), "symphony-tests")),
            new WorkflowHookOptions(
                null,
                null,
                null,
                null,
                60_000),
            new WorkflowAgentOptions(
                1,
                20,
                300_000,
                new Dictionary<string, int>(StringComparer.Ordinal)),
            new WorkflowCodexOptions(
                "codex app-server",
                null,
                null,
                null,
                3_600_000,
                5_000,
                300_000));
    }

    private sealed class StaticWorkflowOptionsProvider(WorkflowServiceOptions workflowOptions) : IWorkflowOptionsProvider
    {
        public Task<WorkflowServiceOptions> GetCurrentAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(workflowOptions);
        }
    }

    private sealed class DelegatePollingIterationHandler(
        Func<WorkflowServiceOptions, CancellationToken, Task> onExecuteAsync) : IPollingIterationHandler
    {
        public Task ExecuteAsync(WorkflowServiceOptions workflowOptions, CancellationToken cancellationToken)
        {
            return onExecuteAsync(workflowOptions, cancellationToken);
        }
    }
}
