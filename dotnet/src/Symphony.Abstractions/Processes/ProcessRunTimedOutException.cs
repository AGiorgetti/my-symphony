namespace Symphony.Abstractions.Processes;

public sealed class ProcessRunTimedOutException : TimeoutException
{
    public ProcessRunTimedOutException(
        string fileName,
        string workingDirectory,
        TimeSpan timeout,
        Exception? innerException = null)
        : base(
            $"Process '{fileName}' timed out after {timeout.TotalMilliseconds:0} ms in '{workingDirectory}'.",
            innerException)
    {
        FileName = fileName;
        WorkingDirectory = workingDirectory;
        Timeout = timeout;
    }

    public string FileName { get; }

    public string WorkingDirectory { get; }

    public TimeSpan Timeout { get; }
}
