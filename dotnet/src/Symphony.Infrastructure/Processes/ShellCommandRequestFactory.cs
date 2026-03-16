using Symphony.Abstractions.Processes;

namespace Symphony.Infrastructure.Processes;

internal static class ShellCommandRequestFactory
{
    public static ProcessRunRequest Create(
        string command,
        string workingDirectory,
        int timeoutMs)
    {
        return OperatingSystem.IsWindows()
            ? new ProcessRunRequest(
                fileName: "pwsh",
                arguments: ["-NoProfile", "-NonInteractive", "-Command", command],
                workingDirectory: workingDirectory,
                timeout: TimeSpan.FromMilliseconds(timeoutMs))
            : new ProcessRunRequest(
                fileName: "sh",
                arguments: ["-lc", command],
                workingDirectory: workingDirectory,
                timeout: TimeSpan.FromMilliseconds(timeoutMs));
    }
}
