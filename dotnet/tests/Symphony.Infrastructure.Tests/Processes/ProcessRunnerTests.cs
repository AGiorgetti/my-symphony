using Microsoft.Extensions.Logging.Abstractions;
using Symphony.Abstractions.Processes;
using Symphony.Infrastructure.Processes;

namespace Symphony.Infrastructure.Tests.Processes;

public sealed class ProcessRunnerTests
{
    private readonly ProcessRunner _runner = new(NullLogger<ProcessRunner>.Instance);

    [Fact]
    public async Task RunAsync_uses_requested_working_directory()
    {
        var workingDirectory = CreateTemporaryDirectory();
        var request = CreateShellRequest(
            OperatingSystem.IsWindows() ? "cd" : "pwd",
            workingDirectory);

        var result = await _runner.RunAsync(request);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(Path.GetFullPath(workingDirectory), result.StandardOutput.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_captures_stdout_stderr_exit_code_and_duration()
    {
        var request = CreateShellRequest(
            OperatingSystem.IsWindows()
                ? "echo stdout message & echo stderr message 1>&2 & exit /b 3"
                : "printf 'stdout message\\n'; printf 'stderr message\\n' 1>&2; exit 3",
            CreateTemporaryDirectory());

        var result = await _runner.RunAsync(request);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("stdout message", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("stderr message", result.StandardError, StringComparison.Ordinal);
        Assert.True(result.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task RunAsync_writes_standard_input_when_requested()
    {
        var request = CreatePipeRequest("agent payload", CreateTemporaryDirectory());

        var result = await _runner.RunAsync(request);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("agent payload", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_applies_environment_variables()
    {
        var request = OperatingSystem.IsWindows()
            ? new ProcessRunRequest(
                "cmd",
                ["/c", "echo %SYMPHONY_RUNNER_TEST%"],
                CreateTemporaryDirectory(),
                environmentVariables: new Dictionary<string, string?> { ["SYMPHONY_RUNNER_TEST"] = "from-env" })
            : new ProcessRunRequest(
                "/bin/sh",
                ["-c", "printf '%s' \"$SYMPHONY_RUNNER_TEST\""],
                CreateTemporaryDirectory(),
                environmentVariables: new Dictionary<string, string?> { ["SYMPHONY_RUNNER_TEST"] = "from-env" });

        var result = await _runner.RunAsync(request);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("from-env", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_terminates_process_when_cancellation_requested()
    {
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var request = CreateShellRequest(
            OperatingSystem.IsWindows()
                ? "ping 127.0.0.1 -n 30 > nul"
                : "sleep 30",
            CreateTemporaryDirectory());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _runner.RunAsync(request, cancellationTokenSource.Token));
    }

    [Fact]
    public async Task RunAsync_terminates_process_when_timeout_expires()
    {
        var request = OperatingSystem.IsWindows()
            ? new ProcessRunRequest("cmd", ["/c", "ping 127.0.0.1 -n 30 > nul"], CreateTemporaryDirectory(), timeout: TimeSpan.FromMilliseconds(200))
            : new ProcessRunRequest("/bin/sh", ["-c", "sleep 30"], CreateTemporaryDirectory(), timeout: TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _runner.RunAsync(request));
    }

    private static ProcessRunRequest CreateShellRequest(string script, string workingDirectory)
    {
        return OperatingSystem.IsWindows()
            ? new ProcessRunRequest("cmd", ["/c", script], workingDirectory)
            : new ProcessRunRequest("/bin/sh", ["-c", script], workingDirectory);
    }

    private static ProcessRunRequest CreatePipeRequest(string input, string workingDirectory)
    {
        return OperatingSystem.IsWindows()
            ? new ProcessRunRequest("cmd", ["/c", "more"], workingDirectory, standardInput: input)
            : new ProcessRunRequest("/bin/sh", ["-c", "cat"], workingDirectory, standardInput: input);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"symphony-process-runner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
