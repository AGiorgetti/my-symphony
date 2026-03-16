using Microsoft.Extensions.Logging.Abstractions;
using Symphony.Application.Configuration;
using Symphony.Application.Polling;

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
            TimeProvider.System,
            NullLogger<PollingBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await iterationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await service.StopAsync(stopCts.Token);
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(capturedToken.CanBeCanceled);
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
