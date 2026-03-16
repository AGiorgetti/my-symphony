using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Symphony.Abstractions.Processes;

namespace Symphony.Infrastructure.Processes;

public sealed class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (request.Timeout is not null)
        {
            linkedCancellationTokenSource.CancelAfter(request.Timeout.Value);
        }

        var effectiveCancellationToken = linkedCancellationTokenSource.Token;
        var startInfo = CreateStartInfo(request);
        using var process = new Process { StartInfo = startInfo };

        var startedAt = DateTimeOffset.UtcNow;
        logger.LogInformation(
            "process_run started file_name={file_name} working_directory={working_directory} argument_count={argument_count} timeout_ms={timeout_ms} outcome=started",
            request.FileName,
            request.WorkingDirectory,
            request.Arguments.Count,
            request.Timeout?.TotalMilliseconds);

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process '{request.FileName}'.");
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(effectiveCancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(effectiveCancellationToken);

        using var cancellationRegistration = effectiveCancellationToken.Register(() => TryKill(process));

        try
        {
            if (request.StandardInput is not null)
            {
                await process.StandardInput.WriteAsync(request.StandardInput.AsMemory(), effectiveCancellationToken).ConfigureAwait(false);
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(effectiveCancellationToken).ConfigureAwait(false);

            var standardOutput = await standardOutputTask.ConfigureAwait(false);
            var standardError = await standardErrorTask.ConfigureAwait(false);
            var finishedAt = DateTimeOffset.UtcNow;
            var result = new ProcessRunResult(process.ExitCode, standardOutput, standardError, startedAt, finishedAt);
            var durationMs = (result.FinishedAt - result.StartedAt).TotalMilliseconds;

            if (result.ExitCode != 0)
            {
                logger.LogWarning(
                    "process_run completed file_name={file_name} exit_code={exit_code} duration_ms={duration_ms} standard_error={standard_error} outcome=failed",
                    request.FileName,
                    result.ExitCode,
                    durationMs,
                    result.StandardError);
            }
            else
            {
                logger.LogInformation(
                    "process_run completed file_name={file_name} exit_code={exit_code} duration_ms={duration_ms} outcome=completed",
                    request.FileName,
                    result.ExitCode,
                    durationMs);
            }

            return result;
        }
        catch (OperationCanceledException) when (effectiveCancellationToken.IsCancellationRequested)
        {
            TryKill(process);

            try
            {
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
            }

            logger.LogInformation(
                "process_run canceled file_name={file_name} working_directory={working_directory} outcome=canceled",
                request.FileName,
                request.WorkingDirectory);
            throw;
        }
    }

    private static ProcessStartInfo CreateStartInfo(ProcessRunRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = request.StandardInput is not null,
            WorkingDirectory = request.WorkingDirectory
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var environmentVariable in request.EnvironmentVariables)
        {
            startInfo.Environment[environmentVariable.Key] = environmentVariable.Value ?? string.Empty;
        }

        return startInfo;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
