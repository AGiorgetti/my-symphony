using Symphony.Abstractions.Processes;

namespace Symphony.Infrastructure.Processes;

internal static class ShellCommandRequestFactory
{
    public static ProcessRunRequest Create(
        string command,
        string workingDirectory,
        int timeoutMs)
    {
        return Create(
            OperatingSystem.IsWindows(),
            command,
            workingDirectory,
            timeoutMs);
    }

    public static ProcessRunRequest Create(
        bool isWindows,
        string command,
        string workingDirectory,
        int timeoutMs)
    {
        return isWindows
            ? new ProcessRunRequest(
                fileName: "powershell",
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
