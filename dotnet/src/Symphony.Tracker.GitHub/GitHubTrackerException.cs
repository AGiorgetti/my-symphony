namespace Symphony.Tracker.GitHub;

public sealed class GitHubTrackerException : Exception
{
    public GitHubTrackerException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
