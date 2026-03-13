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

            if (result.ExitCode != 0)
            {
                logger.LogWarning(
                    "Process '{FileName}' exited with code {ExitCode}. Standard error: {StandardError}",
                    request.FileName,
                    result.ExitCode,
                    result.StandardError);
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
